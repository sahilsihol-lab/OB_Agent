import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass, field
from typing import Iterable, List, Optional
from urllib.parse import quote, urljoin, urlparse

import requests
from bs4 import BeautifulSoup


USER_AGENT = (
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
    "AppleWebKit/537.36 (KHTML, like Gecko) "
    "Chrome/124.0.0.0 Safari/537.36"
)

LIFECYCLE_PATTERNS = {
    "obsolete": [
        r"\bobsolete\b",
        r"\bdiscontinued\b",
        r"\bend\s+of\s+life\b",
        r"\beol\b",
        r"\blast\s+time\s+buy\b",
        r"\bnot\s+recommended\s+for\s+new\s+designs?\b",
        r"\bnrnd\b",
    ],
    "active": [
        r"\bactive\b",
        r"\bin\s+production\b",
        r"\bavailable\b",
        r"\bnew\s+product\b",
    ],
    "unknown": [],
}

STATUS_PRIORITY = {
    "obsolete": 3,
    "active": 2,
    "unknown": 1,
}


@dataclass
class LifecycleSignal:
    status: str
    snippet: str
    source_url: str


@dataclass
class DistributorResult:
    distributor: str
    query_url: str
    status: str = "unknown"
    findings: List[LifecycleSignal] = field(default_factory=list)
    notes: List[str] = field(default_factory=list)


@dataclass
class DistributorConfig:
    name: str
    search_url_template: str
    allowed_hosts: List[str]


DISTRIBUTORS = [
    DistributorConfig(
        name="DigiKey",
        search_url_template="https://www.digikey.com/en/products/result?keywords={query}",
        allowed_hosts=["www.digikey.com", "digikey.com"],
    ),
    DistributorConfig(
        name="Mouser",
        search_url_template="https://www.mouser.com/c/?q={query}",
        allowed_hosts=["www.mouser.com", "mouser.com"],
    ),
    DistributorConfig(
        name="Newark",
        search_url_template="https://www.newark.com/search?st={query}",
        allowed_hosts=["www.newark.com", "newark.com"],
    ),
    DistributorConfig(
        name="Arrow",
        search_url_template="https://www.arrow.com/en/products/search?q={query}",
        allowed_hosts=["www.arrow.com", "arrow.com"],
    ),
    DistributorConfig(
        name="Future Electronics",
        search_url_template="https://www.futureelectronics.com/search?text={query}",
        allowed_hosts=["www.futureelectronics.com", "futureelectronics.com"],
    ),
]


def build_query(part_number: str, manufacturer: Optional[str]) -> str:
    if manufacturer:
        return f"{manufacturer} {part_number}"
    return part_number


def normalize_whitespace(text: str) -> str:
    return re.sub(r"\s+", " ", text).strip()


def extract_text_chunks(html: str) -> List[str]:
    soup = BeautifulSoup(html, "html.parser")
    for tag in soup(["script", "style", "noscript"]):
        tag.decompose()

    text = normalize_whitespace(soup.get_text(" ", strip=True))
    if not text:
        return []

    chunks = re.split(r"(?<=[\.\|\:\;\-])\s+", text)
    return [chunk.strip() for chunk in chunks if chunk.strip()]


def detect_lifecycle_signals(chunks: Iterable[str], source_url: str) -> List[LifecycleSignal]:
    signals: List[LifecycleSignal] = []
    seen = set()

    for chunk in chunks:
        lowered = chunk.lower()
        for status, patterns in LIFECYCLE_PATTERNS.items():
            for pattern in patterns:
                if re.search(pattern, lowered, flags=re.IGNORECASE):
                    snippet = chunk[:300]
                    key = (status, snippet)
                    if key in seen:
                        continue
                    seen.add(key)
                    signals.append(LifecycleSignal(status=status, snippet=snippet, source_url=source_url))
                    break

    signals.sort(key=lambda item: STATUS_PRIORITY[item.status], reverse=True)
    return signals


def summarize_status(signals: List[LifecycleSignal]) -> str:
    if not signals:
        return "unknown"
    return max(signals, key=lambda item: STATUS_PRIORITY[item.status]).status


def is_allowed_product_link(link: str, allowed_hosts: List[str]) -> bool:
    parsed = urlparse(link)
    if parsed.scheme not in {"http", "https"}:
        return False
    host = parsed.netloc.lower()
    return any(host == allowed or host.endswith(f".{allowed}") for allowed in allowed_hosts)


def extract_candidate_links(html: str, base_url: str, allowed_hosts: List[str], part_number: str) -> List[str]:
    soup = BeautifulSoup(html, "html.parser")
    candidates = []
    lowered_part = part_number.lower()

    for anchor in soup.find_all("a", href=True):
        href = urljoin(base_url, anchor["href"])
        text = normalize_whitespace(anchor.get_text(" ", strip=True))
        haystack = f"{href} {text}".lower()
        if lowered_part not in haystack:
            continue
        if not is_allowed_product_link(href, allowed_hosts):
            continue
        if href not in candidates:
            candidates.append(href)
        if len(candidates) >= 5:
            break

    return candidates


def fetch(session: requests.Session, url: str) -> requests.Response:
    response = session.get(url, timeout=20, headers={"User-Agent": USER_AGENT})
    response.raise_for_status()
    return response


def analyze_distributor(
    session: requests.Session,
    distributor: DistributorConfig,
    part_number: str,
    manufacturer: Optional[str],
) -> DistributorResult:
    query = build_query(part_number, manufacturer)
    query_url = distributor.search_url_template.format(query=quote(query))
    result = DistributorResult(distributor=distributor.name, query_url=query_url)

    try:
        response = fetch(session, query_url)
    except requests.RequestException as exc:
        result.notes.append(f"Search request failed: {exc}")
        return result

    search_chunks = extract_text_chunks(response.text)
    search_signals = detect_lifecycle_signals(search_chunks, query_url)
    result.findings.extend(search_signals[:5])

    candidate_links = extract_candidate_links(
        response.text,
        query_url,
        distributor.allowed_hosts,
        part_number,
    )

    for candidate_url in candidate_links:
        try:
            candidate_response = fetch(session, candidate_url)
        except requests.RequestException as exc:
            result.notes.append(f"Product page fetch failed for {candidate_url}: {exc}")
            continue

        page_chunks = extract_text_chunks(candidate_response.text)
        page_signals = detect_lifecycle_signals(page_chunks, candidate_url)
        for signal in page_signals[:5]:
            result.findings.append(signal)

    deduped = []
    dedupe_keys = set()
    for finding in result.findings:
        key = (finding.status, finding.snippet, finding.source_url)
        if key in dedupe_keys:
            continue
        dedupe_keys.add(key)
        deduped.append(finding)

    result.findings = deduped[:10]
    result.status = summarize_status(result.findings)
    if not result.findings and not result.notes:
        result.notes.append("No lifecycle phrase was detected on the inspected pages.")
    return result


def consolidate_results(results: List[DistributorResult]) -> dict:
    all_signals = [signal for result in results for signal in result.findings]
    overall_status = summarize_status(all_signals)

    evidence = []
    for result in results:
        for signal in result.findings[:3]:
            evidence.append(
                {
                    "distributor": result.distributor,
                    "status": signal.status,
                    "snippet": signal.snippet,
                    "source_url": signal.source_url,
                }
            )

    return {
        "overall_status": overall_status,
        "distributor_count": len(results),
        "evidence": evidence[:10],
    }


def render_markdown(part_number: str, manufacturer: Optional[str], results: List[DistributorResult]) -> str:
    summary = consolidate_results(results)
    lines = [
        f"# Lifecycle Analysis: {part_number}",
        "",
        f"- Manufacturer: {manufacturer or 'Not provided'}",
        f"- Overall status: {summary['overall_status']}",
        "",
        "## Distributor findings",
    ]

    for result in results:
        lines.append(f"### {result.distributor}")
        lines.append(f"- Search URL: {result.query_url}")
        lines.append(f"- Status: {result.status}")
        if result.findings:
            for finding in result.findings[:3]:
                lines.append(
                    f"- Evidence: `{finding.status}` from {finding.source_url} -> {finding.snippet}"
                )
        else:
            lines.append("- Evidence: none")
        for note in result.notes[:2]:
            lines.append(f"- Note: {note}")
        lines.append("")

    return "\n".join(lines).strip()


def render_json(part_number: str, manufacturer: Optional[str], results: List[DistributorResult]) -> str:
    payload = {
        "part_number": part_number,
        "manufacturer": manufacturer,
        "summary": consolidate_results(results),
        "results": [asdict(result) for result in results],
    }
    return json.dumps(payload, indent=2)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Analyze product lifecycle status from distributor websites."
    )
    parser.add_argument("part_number", help="Part number to analyze")
    parser.add_argument(
        "--manufacturer",
        help="Optional manufacturer name to improve search precision",
    )
    parser.add_argument(
        "--format",
        choices=["markdown", "json"],
        default="markdown",
        help="Output format",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    session = requests.Session()
    results = [
        analyze_distributor(session, distributor, args.part_number, args.manufacturer)
        for distributor in DISTRIBUTORS
    ]

    if args.format == "json":
        output = render_json(args.part_number, args.manufacturer, results)
    else:
        output = render_markdown(args.part_number, args.manufacturer, results)

    print(output)
    return 0


if __name__ == "__main__":
    sys.exit(main())
