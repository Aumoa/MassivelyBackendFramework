using System.Reflection;

namespace SQLMigration;

internal static class ConsoleExecutor
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrEmpty(args[0]))
        {
            Console.Error.WriteLine("The first argument must contain the ConnectionString.");
            return -1;
        }

        if (args.Length == 1 || string.IsNullOrEmpty(args[1]))
        {
            Console.Error.WriteLine("The second argument must be the path to the DLL file containing the scripts to install.");
            return -1;
        }

        if (args.Length == 2 || string.IsNullOrEmpty(args[2]))
        {
            Console.Error.WriteLine("The third argument must specify the target database.");
            return -1;
        }

        var connectionString = args[0];
        var assemblyName = Path.GetFullPath(args[1]);
        var databaseName = args[2];
        var assembly = Assembly.LoadFile(assemblyName);

        var scriptLoaders = from s in assembly.GetTypes()
                      where s.GetCustomAttribute<DatabaseTargetAttribute>()?.DatabaseName == databaseName
                      select (IScripts?)Activator.CreateInstance(s);
        var scripts = scriptLoaders.FirstOrDefault(s => s != null);

        if (scripts == null)
        {
            Console.Error.WriteLine("There is a Script class without a default constructor or DatabaseTargetAttribute is specified on an invalid target.");
            return -1;
        }

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        try
        {
            await Executor.RunAsync(connectionString, databaseName, [.. scripts.GetScripts()], Console.Out, cts.Token);
        }
        catch (InvalidOperationException e)
        {
            Console.Error.WriteLine(e);
            return -1;
        }

        return 0;
    }
}
