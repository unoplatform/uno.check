---
uid: UnoCheck.UsingUnoCheck
---
<!--markdownlint-disable MD025 -->
# Setup your environment with uno check

[`uno-check`](https://github.com/unoplatform/uno.check) is a dotnet command-line tool that runs a suite of automated check-ups on your dev environment, making sure you have all the prerequisites installed to successfully develop an Uno Platform application. The tool is available on Windows, Linux, and macOS.

If it finds something missing, out of date, or misconfigured, it will either offer to automatically fix it, or else direct you to instructions to manually fix the problem.

![uno-check running](https://github.com/unoplatform/uno/raw/master/doc/articles/Assets/uno-check-running.gif)

## Install and run uno-check

# [**Windows**](#tab/windows)

1. Make sure you have the [.NET SDK installed](https://dotnet.microsoft.com/download/dotnet/latest).
1. Open a command-line prompt, Windows Terminal if you have it installed, or else Command Prompt or Windows Powershell from the Start menu.

    [!INCLUDE [setup-uno-check-inline](./includes/setup-uno-check-inline.md)]

[!INCLUDE [uno-check-after-check](./includes/uno-check-after-check-inline.md)]

# [**Linux**](#tab/linux)

1. Open a Terminal
1. If `dotnet --version` returns `command not found`:
    - Follow the [official directions](https://learn.microsoft.com/dotnet/core/install/linux?WT.mc_id=dotnet-35129-website#packages) for installing .NET.
      > [!IMPORTANT]
      > If your distribution is Ubuntu and you want to develop for Android, make sure to use the directions to install the Microsoft feed and not the Ubuntu official feed. The latter does not contain Android workloads.

    [!INCLUDE [setup-uno-check-inline](./includes/setup-uno-check-inline.md)]

    If the above command fails, use the following:

    ```bash
    ~/.dotnet/tools/uno-check
    ```

[!INCLUDE [uno-check-after-check](./includes/uno-check-after-check-inline.md)]

# [**macOS**](#tab/macos)

1. Make sure you have the [.NET SDK installed](https://dotnet.microsoft.com/download/dotnet/latest).
1. Open a Terminal.

    [!INCLUDE [setup-uno-check-inline](./includes/setup-uno-check-inline.md)]

    If the above command fails, use the following:

    ```bash
    ~/.dotnet/tools/uno-check
    ```

[!INCLUDE [uno-check-after-check](./includes/uno-check-after-check-inline.md)]

***

## See Also

- [Configuration Arguments for Checks](xref:UnoCheck.Configuration)
- [Troubleshooting Uno.Check](xref:UnoCheck.Troubleshooting)

[!INCLUDE [getting-help](./includes/uno-check-help.md)]
