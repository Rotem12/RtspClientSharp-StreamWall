using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;

namespace RtspClientSharp.Web.Services;

public enum PortableLaunchMode
{
    ManagedServer,
    LocalSetup,
    HouseholdShare,
    PublicShare,
    Exit
}

public static class PortableHostStartup
{
    public const string ManagedArgument = "--stream-wall-managed";
    private const int Port = 5085;

    public static PortableLaunchMode SelectMode(string[] arguments)
    {
        if (arguments.Any(argument => string.Equals(argument, ManagedArgument,
                StringComparison.OrdinalIgnoreCase)) ||
            arguments.Length > 0 || Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            return PortableLaunchMode.ManagedServer;
        }

        Console.Title = "Stream Wall";
        Console.WriteLine("Stream Wall");
        Console.WriteLine();
        Console.WriteLine("  1. Configure or edit on this PC");
        Console.WriteLine("  2. Share on the home network (view + edit)");
        Console.WriteLine("  3. Share on the internet (view only, no port forwarding)");
        Console.WriteLine("  Q. Quit");
        Console.WriteLine();
        Console.Write("Choose 1, 2, 3, or Q: ");

        while (true)
        {
            string? choice = Console.ReadLine()?.Trim();
            PortableLaunchMode? mode = choice?.ToLowerInvariant() switch
            {
                "1" => PortableLaunchMode.LocalSetup,
                "2" => PortableLaunchMode.HouseholdShare,
                "3" => PortableLaunchMode.PublicShare,
                "q" => PortableLaunchMode.Exit,
                _ => null
            };

            if (mode is { } selectedMode)
            {
                if (selectedMode != PortableLaunchMode.Exit)
                    Configure(selectedMode);
                return selectedMode;
            }

            Console.Write("Please enter 1, 2, 3, or Q: ");
        }
    }

    public static void PrintLocalAddress(PortableLaunchMode mode)
    {
        Console.WriteLine();
        if (mode == PortableLaunchMode.LocalSetup)
        {
            string localUrl = $"http://127.0.0.1:{Port}/view/default";
            Console.WriteLine($"Local editor: {localUrl}");
            Console.WriteLine("Only this PC can open that address. Press Ctrl+C when finished.");
            OpenBrowser(localUrl);
            return;
        }

        Console.WriteLine("Home-network links (open one on another device to view or edit):");
        IReadOnlyList<IPAddress> addresses = GetLanAddresses();
        if (addresses.Count == 0)
            Console.WriteLine("  No home-network IPv4 address was detected.");
        else
        {
            foreach (IPAddress address in addresses)
                Console.WriteLine($"  http://{address}:{Port}/view/default");
        }

        Console.WriteLine("Anyone on your home network can edit this wall. A configured editor PIN still applies.");
        Console.WriteLine("Press Ctrl+C to stop sharing.");
        OpenBrowser($"http://127.0.0.1:{Port}/view/default");
    }

    private static void Configure(PortableLaunchMode mode)
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory;
        string url = mode switch
        {
            PortableLaunchMode.LocalSetup => $"http://127.0.0.1:{Port}",
            PortableLaunchMode.HouseholdShare => $"http://0.0.0.0:{Port}",
            PortableLaunchMode.PublicShare => $"http://127.0.0.1:{Port}",
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };

        Environment.SetEnvironmentVariable("ASPNETCORE_URLS", url, EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("StreamWall__ReadOnly",
            mode == PortableLaunchMode.PublicShare ? "true" : "false", EnvironmentVariableTarget.Process);
        Environment.SetEnvironmentVariable("StreamWall__AccessToken", string.Empty, EnvironmentVariableTarget.Process);
    }

    private static IReadOnlyList<IPAddress> GetLanAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                                  adapter.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211)
                .SelectMany(adapter => adapter.GetIPProperties().UnicastAddresses)
                .Select(address => address.Address)
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(address) &&
                                  !address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .Distinct()
                .ToArray();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<IPAddress>();
        }
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            Console.WriteLine($"Open this address in a browser: {url} ({exception.Message})");
        }
    }
}
