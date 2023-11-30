using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DotNetCheck;

public class LinuxPackageManagerWrapper
{
    public string Label { get; }
    public string InstallExecutable { get; }
    public string UpdateExecutable { get; }
    public string SearchExecutable { get; }
    
    public string InstallFormat { get; }
    public string UpdateFormat { get; }
    public string SearchFormat { get; }
    public bool NeedsSudoForInstall { get; }
    public bool NeedsSudoForUpdate { get; }
    public bool NeedsSudoForSearch { get; }

    public LinuxPackageManagerWrapper(string label, string installExecutable, string updateExecutable, string searchExecutable, string installFormat, string updateFormat, string searchFormat, bool needsSudoForInstall, bool needsSudoForUpdate, bool needsSudoForSearch)
    {
        Label = label;
        InstallExecutable = installExecutable;
        UpdateExecutable = updateExecutable;
        SearchExecutable = searchExecutable;
        InstallFormat = installFormat;
        UpdateFormat = updateFormat;
        SearchFormat = searchFormat;
        NeedsSudoForInstall = needsSudoForInstall;
        NeedsSudoForUpdate = needsSudoForUpdate;
        NeedsSudoForSearch = needsSudoForSearch;
    }

    public static IEnumerable<(LinuxPackageManagerWrapper wrapper, string packageName)> MatchPackageNamesWithStandardSupport(
        string debianName,
        string archName,
        string fedoraRhelName,
        string oldFedoraRhelName,
        string openSuseName
        )
    {
        var packageNames = new[]
        {
            debianName,
            archName,
            fedoraRhelName,
            oldFedoraRhelName,
            openSuseName
        };

        Debug.Assert(packageNames.Length == StandardSupport.Count());

        return StandardSupport.Zip(packageNames);
    }

    public static IEnumerable<LinuxPackageManagerWrapper> StandardSupport => new[]
    {
        Debian,
        Arch,
        FedoraRHEL,
        OldFedoraRHEL,
        OpenSUSE
    };

    public static LinuxPackageManagerWrapper Debian { get; } = new LinuxPackageManagerWrapper(
        nameof(Debian),
        "apt-get",
        "apt-get",
        "apt",
        "install -y {0}",
        "update",
        "search ^{0}$",
        true,
        true,
        false);
    
    public static LinuxPackageManagerWrapper Arch { get; } = new LinuxPackageManagerWrapper(
        nameof(Arch),
        "pacman",
        "pacman",
        "[",
        "-S --noconfirm {0}",
        "-Sy",
        "\"$(pacman -Sqs {0} | grep '^{0}$' | wc -l)\" -eq 1 ]", // a hack to search for an exact match
        true,
        true,
        false);
    
    // TODO
    public static LinuxPackageManagerWrapper FedoraRHEL { get; } = new LinuxPackageManagerWrapper(
        nameof(FedoraRHEL),
        "dnf",
        "dnf",
        "[",
        "install -y {0}",
        "makecache",
        "\"$(dnf repoquery --queryformat '%{NAME}' {0} | grep '^{0}$' | wc -l)\" -eq 1 ]", // a hack to search for an exact match
        true,
        true,
        false);
    
    // TODO
    public static LinuxPackageManagerWrapper OldFedoraRHEL { get; } = new LinuxPackageManagerWrapper(
        nameof(OldFedoraRHEL),
        "yum",
        "yum",
        "yum",
        "install -y {0}",
        "makecache",
        "list {0}",
        true,
        true,
        false);
    
    // TODO
    public static LinuxPackageManagerWrapper OpenSUSE { get; } = new LinuxPackageManagerWrapper(
        nameof(OpenSUSE),
        "zypper",
        "zypper",
        "zypper",
        "-n install {0}",
        "refresh",
        "se --match-exact {0}",
        true,
        true,
        false);

    public async Task<bool> Update()
    {
        var result = NeedsSudoForUpdate ?
            await Util.WrapShellCommandWithSudo(UpdateExecutable, null, true, UpdateFormat.Split(" ")) : 
            ShellProcessRunner.Run(UpdateExecutable, UpdateFormat);

        return result.Success;
    }
    
    public async Task<bool> SearchForPackage(string packageName, bool updateFirst = true)
    {
        if (updateFirst)
        {
            var updateResult = await Update();

            if (!updateResult)
            {
                return false;
            }
        }
        
        var searchResult =
            NeedsSudoForSearch ?
                await Util.WrapShellCommandWithSudo(SearchExecutable, null, true, string.Format(SearchFormat, packageName).Split(" ")) :
                ShellProcessRunner.Run(SearchExecutable, string.Format(SearchFormat, packageName));

        return searchResult.Success;
    }

    public async Task<bool> InstallPackage(string packageName, bool updateFirst = true)
    {
        if (updateFirst)
        {
            var updateResult = await Update();

            if (!updateResult)
            {
                return false;
            }
        }
        
        var installResult =
            NeedsSudoForInstall ?
            await Util.WrapShellCommandWithSudo(InstallExecutable, null, true, string.Format(InstallFormat, packageName).Split(" ")) :
            ShellProcessRunner.Run(InstallExecutable, string.Format(InstallFormat, packageName));

        return installResult.Success;
    }

    public static async Task<bool> InstallPackage(IEnumerable<(LinuxPackageManagerWrapper wrapper, string packageName)> distros, bool updateFirst)
    {
        Debug.Assert(distros.Count() != 0);
        
        foreach (var distro in distros)
        {
            var wrapper = distro.wrapper;
            if (IsCorrectWrapper(wrapper))
            {
                if (await wrapper.InstallPackage(distro.packageName, updateFirst))
                {
                    Util.Log($"Installed {distro.packageName} using {wrapper.SearchExecutable}");
                    return true;
                }
                else
                {
                    Util.Log($"Found {wrapper.Label}, but couldn't install {distro.packageName}");
                    return false;
                }
            }
            else
            {
                Util.Log($"Installing: Couldn't find {distro.wrapper.Label}, moving on");
            }
        }
        
        Util.Log($"Couldn't find any package manager executable to install {distros.First().packageName}");
        return false;
    }
    
    public static async Task<bool> SearchForPackage(IEnumerable<(LinuxPackageManagerWrapper wrapper, string packageName)> distros, bool updateFirst)
    {
        Debug.Assert(distros.Count() != 0);
        
        foreach (var distro in distros)
        {
            var wrapper = distro.wrapper;
            if (IsCorrectWrapper(distro.wrapper))
            {
                if (await wrapper.SearchForPackage(distro.packageName, updateFirst))
                {
                    Util.Log($"Found {distro.packageName} using {wrapper.SearchExecutable}");
                    return true;
                }
                else
                {
                    Util.Log($"Found {wrapper.Label}, but couldn't find {distro.packageName}");
                    return false;
                }
            }
            else
            {
                Util.Log($"Searching: Couldn't detect {wrapper.Label}, moving on");
            }
        }
        
        return false;
    }

    private static bool IsCorrectWrapper(LinuxPackageManagerWrapper wrapper)
    {
        return ShellProcessRunner.Run("which", wrapper.SearchExecutable).Success &&
               ShellProcessRunner.Run("which", wrapper.UpdateExecutable).Success &&
               ShellProcessRunner.Run("which", wrapper.InstallExecutable).Success;
    }
}