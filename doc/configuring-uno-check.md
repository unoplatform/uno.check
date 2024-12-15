# Configuring Uno Check

## Running Uno.Check in a CI environment

It is possible to run Uno.Check to setup your build environment in a repeatable way by using the following commands:

# [**Windows**](#tab/windows)

```bash
dotnet tool install --global Uno.Check --version 1.20.0
uno-check -v --ci --non-interactive --fix --skip vswin --skip androidemulator --skip androidsdk
```

# [**macOS**](#tab/macos)

```bash
dotnet tool install --global Uno.Check --version 1.20.0
uno-check -v --ci --non-interactive --fix --skip xcode --skip androidemulator --skip androidsdk
```


# [**Linux**](#tab/linux)

```bash
dotnet tool install --global Uno.Check --version 1.20.0
uno-check -v --ci --non-interactive --fix --skip androidemulator
```

***

Pinning uno.check to a specific version will allow to keep a repeatable build over time, regardless of the updates done to Uno Platform or .NET. Make sure to regularly update to a more recent version of Uno.Check.

> [!TIP]
> You can use `dotnet package search uno.check` to search for the latest version of uno.check.

## Running without elevation on Windows

In restricted environments, it may be required to run uno-check to determine what needs to be installed without privileges elevation.

In order to do so, use the following command:

```bash
cmd /c "set __COMPAT_LAYER=RUNASINVOKER && uno-check"
```

## Command line arguments

The following command line arguments can be used to customize the tool's behavior.

### `--target` Choose target platforms

Uno Platform supports a number of platforms, and you may only wish to develop for a subset of them. By default, the tool runs check for all supported platforms. If you use the `--target` argument, it will only run checks for the nominated target or targets.

So, for example, the following will only check your environment for web and Linux development:

```bash
uno-check --target wasm --target linux
```

The following argument values for `--target` are supported:

| Value     | Comments          |
|-----------|-------------------|
| wasm      |                   |
| ios       |                   |
| android   |                   |
| macos     |                   |
| linux     |                   |
| skiawpf   |                   |
| uwp       |                   |
| win32     |                   |
| all       | All platforms     |

### `-m <FILE_OR_URL>`, `--manifest <FILE_OR_URL>` Manifest File or Url

The manifest file is used by the tool to fetch the latest versions and requirements.
The default manifest is hosted at: `https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui.manifest.json`

Use this option to specify an alternative file path or URL to use.

```bash
uno-check --manifest /some/other/file
```

### `-f`, `--fix` Fix without prompt

You can try using the `--fix` argument to automatically enable solutions to run without being prompted.

```bash
uno-check --fix
```

### `-n`, `--non-interactive` Non-Interactive

If you're running on CI, you may want to run without any required input with the `--non-interactive` argument.  You can combine this with `--fix` to automatically fix without prompting.

```bash
uno-check --non-interactive
```

### `--pre`, `--preview`, `-d`, `--dev` Preview Manifest feed

This uses a more frequently updated manifest with newer versions of things more often. If you use the prerelease versions of Uno.UI NuGet packages, you should use this flag.

The manifest is hosted by default at: `https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui-preview.manifest.json`

```bash
uno-check --pre
```

### `--pre-major`, `--preview-major`

This generally uses the preview builds of the next major version of .NET available.

The manifest is hosted by default at: `https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui-preview-major.manifest.json`

```bash
uno-check --pre
```

### `--ci` Continuous Integration

Uses the dotnet-install powershell / bash scripts for installing the dotnet SDK version from the manifest instead of the global installer.

```bash
uno-check --ci
```

### `-s <ID_OR_NAME>`, `--skip <ID_OR_NAME>` Skip Checkup

Skips a checkup by name or id as listed in `uno-check list`.

> [!NOTE]
> If there are any other checkups which depend on a skipped checkup, they will be skipped too.

```bash
uno-check --skip openjdk --skip androidsdk
```

### `list` List Checkups

Lists possible checkups in the format: `checkup_id (checkup_name)`.
These can be used to specify `--skip checkup_id`, `-s checkup_name` arguments.

### `config` Configure global.json and NuGet.config in Working Dir

This allows you to quickly synchronize your `global.json` and/or `NuGet.config` in the current working directory to utilize the values specified in the manifest.

Arguments:

- `--dotnet` or `--dotnet-version`: Use the SDK version in the manifest in `global.json`.
- `--dotnet-pre true|false`: Change the `allowPrerelease` value in the `global.json`.
- `--dotnet-rollForward <OPTION>`: Change the `rollForward` value in `global.json` to one of the allowed values specified.
- `--nuget` or `--nuget-sources`: Adds the nuget sources specified in the manifest to the `NuGet.config` and creates the file if needed.

Example:

```bash
uno-check config --dev --nuget-sources --dotnet-version --dotnet-pre true
```
