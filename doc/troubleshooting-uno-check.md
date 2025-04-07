---
uid: UnoCheck.Troubleshooting
---

# Troubleshooting Uno Check

If you run into problems with uno-check, you should generally try the following:

1. Update the tool to the latest version: `dotnet tool update -g uno.check --add-source https://api.nuget.org/v3/index.json`
1. If you are running with .NET 8 and workloads are causing issues (e.g. missing workloads even when everything seems installed), try running the following command:

    ```dotnetcli
    dotnet workload clean --all
    ```

  This command will clean all workloads for all installed .NET versions. This will allow `uno-check` to reinstall them properly.
  If the `dotnet workload clean` tells that some workloads can't be removed, try using the `repair` command in the Visual Studio installer as well.

1. Run with `uno-check --force-dotnet` to ensure the workload repair/update/install commands run regardless of if uno-check thinks the workload versions look good.
1. If you encounter the error `Unable to load the service index` when installing `uno-check` for a host name not ending by `nuget.org`, try using the `--ignore-failed-sources` parameter.
1. If you still have errors, it may help to run the [Clean-Old-DotNet6-Previews.ps1](https://github.com/unoplatform/uno.check/blob/main/Clean-Old-DotNet6-Previews.ps1) script to remove old SDK Packs, templates, or otherwise old cached preview files that might be causing the problem.  Try running `uno-check --force-dotnet` again after this step.
1. If you encounter the message `There were one or more problems detected. Please review the errors and correct them and run uno-check again.`, but if you have done all the previously mentioned steps, try running it with `--verbose` flag to see where it is failing.
1. Finally, if you have other problems, run also with `--verbose` flag and capture the output and add it to a new issue.
