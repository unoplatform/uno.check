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

1. Run the tool from the command prompt with the following command:

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

