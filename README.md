
# Uno.Check
[![NuGet](https://badgen.net/nuget/v/Uno.Check)](https://www.nuget.org/packages/Uno.Check)

**Uno.Check** is a cross-platform, command-line .NET tool that validates and configures your development environment in a single step.
Once installed as a global tool, it runs a series of “checkups”, reports any missing or misconfigured components, and can apply automatic fixes.
Designed to simplify setup and ensures you have everything you need to build and debug Uno apps with confidence.



To install the tool:
```
dotnet tool install -g Uno.Check
```

To update the tool, if you already have an existing one:
```
dotnet tool update -g uno.check
```

To run the tool:
```
uno-check
```

![uno-check running](https://github.com/unoplatform/uno/raw/master/doc/articles/Assets/uno-check-running.gif)

Visit our [documentation](doc/using-uno-check.md) for more details.

## Contributing

Thanks for helping improve Uno.Check — your contributions make a difference! ❤️

### Clone the repository

```bash
git clone https://github.com/unoplatform/uno.check.git
cd uno.check
```

### Run your IDE as Administrator

Uno.Check requires **Administrator** permissions to run so make sure to run your IDE elevated.

### Configure launchSettings.json

If you need to pass custom arguments update [launchSettings.json](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/Properties/launchSettings.json) profiles accordingly.


### Build & install a local version

We include a helper script to pack and install your locally built `uno.check` as a global tool, so you can test changes immediately on your machine:

```powershell
# From the repo root:
./pack-and-install.ps1
```


### Spectre.Console

This CLI is built on [Spectre.Console](https://spectreconsole.net/) — feel free to explore their docs for examples.



---

Based on [Redth's .NET MAUI Check tool](https://github.com/Redth/dotnet-maui-check).

---
