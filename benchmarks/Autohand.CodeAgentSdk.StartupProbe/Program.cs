using System.Diagnostics;
using System.Globalization;
using System.Reflection;

if (args.Length != 1)
{
    throw new ArgumentException("Expected the SDK assembly path.");
}

var timer = Stopwatch.StartNew();
var assembly = Assembly.LoadFrom(args[0]);
_ = assembly.GetType("Autohand.CodeAgentSdk.AutohandSdk", throwOnError: true);
timer.Stop();
Console.WriteLine(timer.Elapsed.TotalMilliseconds.ToString("F6", CultureInfo.InvariantCulture));
