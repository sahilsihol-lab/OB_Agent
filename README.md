# OB Agent

`ver-1.0` contains a Windows desktop app for part lifecycle research plus the original Python CLI.

## Included tools

- `PartLifecycleDesktop`: WPF desktop app for Windows
- `product_lifecycle_agent.py`: Python CLI for distributor lifecycle checks

## Desktop app features

- Paste one part number per line
- Checks DigiKey first, then Mouser, then fallback distributor/manufacturer sources
- Uses DigiKey API and Mouser API when configured
- Shows manufacturer, lifecycle status, summary, and source evidence
- Supports aborting an in-progress analysis
- Exports results to Excel (`.xlsx`)
- Displays a company badge in the app header

## API configuration

Do not commit live credentials. Put these files beside the published `.exe` on your own machine:

- `digikey-api.json`
- `mouser-api.json`

Sample templates are included in `PartLifecycleDesktop/`:

- `digikey-api.sample.json`
- `mouser-api.sample.json`

## Build

```powershell
$env:DOTNET_CLI_HOME=".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1"
$env:DOTNET_NOLOGO="1"
dotnet build .\PartLifecycleDesktop\PartLifecycleDesktop.csproj -c Release
```

## Notes

- The desktop app is Windows-only because it is built with WPF.
- Distributor pages can change or block scraping, so API-backed results are preferred when available.
- Published app folders, local API JSON files, and generated build outputs are intentionally excluded from this repo.
