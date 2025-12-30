using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;

CultureInfo newCulture = new("en-US");
Thread.CurrentThread.CurrentCulture = newCulture;

var stopwatch = new Stopwatch();
stopwatch.Start();

var filePath = Path.Combine("/Users/teis/code/1brc-files", "measurements-full.txt"); // measurements-million.txt

var mmap = MemoryMappedFile.CreateFromFile(filePath);

using var fs = mmap.CreateViewStream();

using var reader = new StreamReader(fs, Encoding.UTF8);

var hm = new OrderedDictionary<string, Measurement>();

string line;
while ((line = await reader.ReadLineAsync()) != null)
{
    var split = line.Split(';');

    if (hm.ContainsKey(split[0]))
    {
        hm[split[0]].Add(Convert.ToDouble(split[1]));
    }
    else
    {
        var tmp = new Measurement();
        tmp.Add(Convert.ToDouble(split[1]));
        hm.Add(split[0], tmp);
    }
}

foreach (KeyValuePair<string, Measurement> v in hm.OrderBy(o => o.Key))
{
    Console.WriteLine($"{v.Key};{v.Value.ToString()}");
}

stopwatch.Stop();
TimeSpan elapsed = stopwatch.Elapsed;
string formatTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            elapsed.Hours, elapsed.Minutes, elapsed.Seconds,
            elapsed.Milliseconds / 10);

Console.WriteLine($"RunTime: {formatTime}");
Console.WriteLine($"Elapsed milliseconds: {stopwatch.ElapsedMilliseconds}");

internal sealed class Measurement
{
    private int _count = 0;
    private double _sum = 0;
    private double _min = 0.0;
    private double _max = 0.0;

    public void Add(double value)
    {
        _count++;
        _sum += value;
        if (_min > value) _min = value;
        if (_max < value) _max = value;
    }

    public override string ToString()
    {
        return $"{_min};{(_sum/_count).ToString("F2")};{_max}";
    }
}