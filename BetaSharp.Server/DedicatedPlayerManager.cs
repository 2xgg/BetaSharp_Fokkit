using Microsoft.Extensions.Logging;

namespace BetaSharp.Server;

internal class DedicatedPlayerManager : PlayerManager
{
    private readonly ILogger<DedicatedPlayerManager> _logger = Log.Instance.For<DedicatedPlayerManager>();
    private readonly string _bannedPlayersFile;
    private readonly string _bannedIpsFile;
    private readonly string _operatorsFile;
    private readonly string _whitelistFile;

    public DedicatedPlayerManager(MinecraftServer server) : base(server)
    {
        _bannedPlayersFile = server.GetFilePath("banned-players.txt");
        _bannedIpsFile = server.GetFilePath("banned-ips.txt");
        _operatorsFile = server.GetFilePath("ops.txt");
        _whitelistFile = server.GetFilePath("white-list.txt");

        loadBannedPlayers();
        loadBannedIps();
        loadOperators();
        loadWhitelist();
        saveBannedPlayers();
        saveBannedIps();
        saveOperators();
        saveWhitelist();
    }

    protected override void loadBannedPlayers()
    {
        try
        {
            bannedPlayers.Clear();
            if (File.Exists(_bannedPlayersFile))
            {
                foreach (string line in File.ReadAllLines(_bannedPlayersFile))
                {
                    string trimmed = line.Trim().ToLower();
                    if (trimmed.Length > 0)
                        bannedPlayers.Add(trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load ban list: {ex}");
        }
    }

    protected override void saveBannedPlayers()
    {
        try
        {
            File.WriteAllLines(_bannedPlayersFile, bannedPlayers);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to save ban list: {ex}");
        }
    }

    protected override void loadBannedIps()
    {
        try
        {
            bannedIps.Clear();
            if (File.Exists(_bannedIpsFile))
            {
                foreach (string line in File.ReadAllLines(_bannedIpsFile))
                {
                    string trimmed = line.Trim().ToLower();
                    if (trimmed.Length > 0)
                        bannedIps.Add(trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load ip ban list: {ex}");
        }
    }

    protected override void saveBannedIps()
    {
        try
        {
            File.WriteAllLines(_bannedIpsFile, bannedIps);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to save ip ban list: {ex}");
        }
    }

    protected override void loadOperators()
    {
        try
        {
            ops.Clear();
            if (File.Exists(_operatorsFile))
            {
                foreach (string line in File.ReadAllLines(_operatorsFile))
                {
                    string trimmed = line.Trim().ToLower();
                    if (trimmed.Length > 0)
                        ops.Add(trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load operators list: {ex}");
        }
    }

    protected override void saveOperators()
    {
        try
        {
            File.WriteAllLines(_operatorsFile, ops);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to save operators list: {ex}");
        }
    }

    protected override void loadWhitelist()
    {
        try
        {
            whitelist.Clear();
            if (File.Exists(_whitelistFile))
            {
                foreach (string line in File.ReadAllLines(_whitelistFile))
                {
                    string trimmed = line.Trim().ToLower();
                    if (trimmed.Length > 0)
                        whitelist.Add(trimmed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to load white-list: {ex}");
        }
    }

    protected override void saveWhitelist()
    {
        try
        {
            File.WriteAllLines(_whitelistFile, whitelist);
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Failed to save white-list: {ex}");
        }
    }
}
