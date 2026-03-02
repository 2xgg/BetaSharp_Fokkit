namespace BetaSharp.Server.Threading;

public class ConsoleInputThread
{
    private readonly MinecraftServer _mcServer;
    private readonly Thread _thread;

    public ConsoleInputThread(MinecraftServer server)
    {
        _mcServer = server;
        _thread = new Thread(Run)
        {
            Name = "Server console handler",
            IsBackground = true
        };
    }

    public void Start() => _thread.Start();

    private void Run()
    {
        while (!_mcServer.stopped && _mcServer.running)
        {
            string? line = Console.ReadLine();
            if (line != null)
            {
                _mcServer.queueCommands(line, _mcServer);
            }
        }
    }
}
