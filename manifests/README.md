# Manifest Files

These are used by the tool to parse up-to-date versions and information on which things to validate and install.

They are grouped into the following channels:

### Default
This should align with the current MAUI stable version available:
- maui.manifest.json
- https://aka.ms/dotnet-maui-check-manifest

### Preview
This should align with the current MAUI preview version available:
- maui-preview.manifest.json
- https://aka.ms/dotnet-maui-check-manifest-preview

### Main
This should align with the current MAUI main branch version available and will change often:
- maui-main.manifest.json
- https://aka.ms/dotnet-maui-check-manifest-main

## Updating the Manifest

To update the manifest, the usual process is:

1. Use a clean VM and install the latest .NET version.
2. Run:
   ```bash
   dotnet workload install ios android maccatalyst tvos maui wasm-tools
   ```
3. Run:
   ```bash
   dotnet workload list
   ```
4. Use the output to update the manifest with the correct versions.
