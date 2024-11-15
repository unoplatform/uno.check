---
uid: UnoCheck.UsingUnoCheck
---

# Setup your environment with uno check

[`uno-check`](https://github.com/unoplatform/uno.check) is a dotnet command-line tool that runs a suite of automated check-ups on your dev environment, making sure you have all the prerequisites installed to successfully develop an Uno Platform application. The tool is available on Windows, Linux, and macOS.

If it finds something missing, out of date, or misconfigured, it will either offer to automatically fix it, or else direct you to instructions to manually fix the problem.

![uno-check running](https://github.com/unoplatform/uno/raw/master/doc/articles/Assets/uno-check-running.gif)

## Install and run uno-check

# [**Windows**](#tab/windows)

1. Make sure you have the [.NET SDK installed](https://dotnet.microsoft.com/download).
1. Open a command-line prompt, Windows Terminal if you have it installed, or else Command Prompt or Windows Powershell from the Start menu.
1. Setup uno.check by:
    - Installing the tool:

        ```dotnetcli
        dotnet tool install -g uno.check
        ```

    - Updating the tool, if you previously installed it:

        ```dotnetcli
        dotnet tool update -g uno.check
        ```

1. Run the tool from the command prompt (as administrator) with the following command:

    ```bash
    uno-check
    ```

1. Follow the instructions indicated by the tool.
1. If you get any errors or warnings, run the provided fix or follow the provided instructions. Run `uno-check` again to verify that the fixes worked.
1. Once `uno-check` gives you the green light, you can [get started](https://platform.uno/docs/articles/get-started.html)!

# [**Linux**](#tab/linux)

1. Open a Terminal
1. If `dotnet --version` returns `command not found`:
    - Follow the [official directions](https://learn.microsoft.com/dotnet/core/install/linux?WT.mc_id=dotnet-35129-website#packages) for installing .NET.
      > [!IMPORTANT]
      > If your distribution is Ubuntu and you want to develop for Android, make sure to use the directions to install the Microsoft feed and not the Ubuntu official feed. The latter does not contain Android workloads.
1. Then, setup uno.check by:
    - Installing the tool:

        ```dotnetcli
        dotnet tool install -g uno.check
        ```

    - Updating the tool, if you previously installed it:

        ```dotnetcli
        dotnet tool update -g uno.check
        ```

1. Run the tool from the command prompt with the following command:

    ```bash
    uno-check
    ```

    If the above command fails, use the following:

    ```bash
    ~/.dotnet/tools/uno-check
    ```

1. Follow the instructions indicated by the tool
1. If you get any errors or warnings, run the provided fix or follow the provided instructions. Run `uno-check` again to verify that the fixes worked.
1. Once `uno-check` gives you the green light, you can [get started](https://platform.uno/docs/articles/get-started.html)!

# [**macOS**](#tab/macos)

1. Make sure you have the [.NET SDK installed](https://dotnet.microsoft.com/download).
1. Open a Terminal.
1. Setup uno.check by:
    - Installing the tool:

        ```dotnetcli
        dotnet tool install -g uno.check
        ```

    - Updating the tool, if you previously installed it:

        ```dotnetcli
        dotnet tool update -g uno.check
        ```

1. Run the tool from the command prompt with the following command:

    ```bash
    uno-check
    ```

    If the above command fails, use the following:

    ```bash
    ~/.dotnet/tools/uno-check
    ```

1. Follow the instructions indicated by the tool
1. If you get any errors or warnings, run the provided fix or follow the provided instructions. Run `uno-check` again to verify that the fixes worked.
1. Once `uno-check` gives you the green light, you can [get started](https://platform.uno/docs/articles/get-started.html)!

***

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

## Troubleshooting

If you run into problems with uno-check, you should generally try the following:

1. Update the tool to the latest version: `dotnet tool update -g uno.check --add-source https://api.nuget.org/v3/index.json`
1. If you are running with .NET 8, and workloads are causing issues (e.g. missing workloads even when everything seems installed), try running the following command:

  ```dotnetcli
  dotnet workload clean --all
  ```

  This command will clean all workloads for all installed .NET versions. This will allow `uno-check` to reinstall them properly.
  If the `dotnet workload clean` tells that some workloads can't be removed, try using the `repair` command in the Visual Studio installer as well.

1. Run with `uno-check --force-dotnet` to ensure the workload repair/update/install commands run regardless of if uno-check thinks the workload versions look good.
1. If you encounter the error `Unable to load the service index` when installing `uno-check` for a host name not ending by `nuget.org`, try using the `--ignore-failed-sources` parameter.
1. If you still have errors, it may help to run the [Clean-Old-DotNet6-Previews.ps1](https://github.com/unoplatform/uno.check/blob/main/Clean-Old-DotNet6-Previews.ps1) script to remove old SDK Packs, templates, or otherwise old cached preview files that might be causing the problem.  Try running `uno-check --force-dotnet` again after this step.
1. Finally, if you have other problems, run with `--verbose` flag and capture the output and add it to a new issue.

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
