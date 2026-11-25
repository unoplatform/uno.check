---
uid: UnoCheck.Troubleshooting
---

# Troubleshooting Uno Check

If you run into problems with uno-check, you should generally try the following:

1. Update the tool to the latest version: `dotnet tool update -g uno.check --add-source https://api.nuget.org/v3/index.json`
1. If you are running with .NET 9+, and workloads are causing issues (e.g. missing workloads even when everything seems installed), try running the following command:

    ```dotnetcli
    dotnet workload clean --all
    ```

  This command will clean all workloads for all installed .NET versions. This will allow `uno-check` to reinstall them properly.
  If the `dotnet workload clean` tells that some workloads can't be removed, try using the `repair` command in the Visual Studio installer as well.

1. If you encounter build errors such as: `error MSB4022: The result "" of evaluating the value "$(MonoTargetsTasksAssemblyPath)" of the "AssemblyFile" attribute in element <UsingTask> is not valid.`, it may help to run the following command:

    ```dotnetcli
    dotnet workload repair
    ```

  This can resolve issues where uno-check does not surface any problems but builds still fail.

1. Run with `uno-check --force-dotnet` to ensure the workload repair/update/install commands run regardless of if uno-check thinks the workload versions look good.
1. If you encounter the error `Unable to load the service index` when installing `uno-check` for a host name not ending by `nuget.org`, try using the `--ignore-failed-sources` parameter.
1. If you still have errors, it may help to run the [Clean-Old-DotNet6-Previews.ps1](https://github.com/unoplatform/uno.check/blob/main/Clean-Old-DotNet6-Previews.ps1) script to remove old SDK Packs, templates, or otherwise old cached preview files that might be causing the problem.  Try running `uno-check --force-dotnet` again after this step.
1. If you encounter a message similar to `Uno.Check version support mismatch detected` and you are running with `--ci`, update the tool first:

    ```dotnetcli
    dotnet tool update -g uno.check --add-source https://api.nuget.org/v3/index.json
    ```

    Then rerun `uno-check`.

1. If you encounter the message `There were one or more problems detected. Please review the errors and correct them and run uno-check again.`, but if you have done all the previously mentioned steps, try running it with `--verbose` flag to see where it is failing.
1. Finally, if you have other problems, run also with `--verbose` flag and capture the output and add it to a new issue.

## NuGet sources

uno-check follows the NuGet configuration that applies to the directory it runs from, like the rest of the .NET tooling: the `nuget.config` files in that directory and its parents, then the user and machine level settings. Disabled sources are skipped, and `<clear />` removes the default nuget.org source. This applies to the latest-version lookups (uno-check, the Uno templates, Uno.Sdk), to the Uno.Sdk package download and restore, and to `dotnet workload` commands.

- If nothing customizes the sources, nuget.org is used as before.
- A source that can't be reached doesn't fail a lookup that another configured source can answer. The optional update and latest-version checks are skipped quietly, and the reason is shown with `--verbose`.
- The sources listed in the manifest (for example the .NET preview feed) are added to the configured ones for `dotnet workload` commands. The public nuget.org feed in the manifest is only used when your configuration includes it.
- Workload commands run from a temporary directory, so uno-check gives them the configured sources through a temporary `nuget.config` (private to the current user on Linux and macOS), with the per-source `allowInsecureConnections` and `disableTLSCertificateValidation` settings carried over. A source your configuration disables, or a configuration that clears all sources, is respected. Credentials that are only stored in a repository's `nuget.config` are not copied there; credentials in your user level settings or from a credential provider still apply.
- On Linux and macOS, when the .NET SDK is in a location that needs `sudo`, `dotnet workload` commands read root's user level NuGet settings instead of yours. A feed that is authenticated only through your own user level settings, a credential provider installed for your user, or environment variables isn't authenticated for those commands.
