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

var hm = new Dictionary<byte[], Measurement>(new ByteArrayComparer());

var nameBuffer = new List<byte>();
var valueBuffer = new List<byte>();
var buffer = new byte[8192];
int bytesRead;
int bufferPos = 0;
int bufferEnd = 0;

byte GetNextByte()
{
    if (bufferPos >= bufferEnd)
    {
        bytesRead = fs.Read(buffer, 0, buffer.Length);
        if (bytesRead == 0) return 0;
        bufferPos = 0;
        bufferEnd = bytesRead;
    }
    return buffer[bufferPos++];
}

while (true)
{
    // Read station name until ';'
    nameBuffer.Clear();
    byte b;
    while ((b = GetNextByte()) != 0 && b != (byte)';')
    {
        nameBuffer.Add(b);
    }

    if (b == 0) break; // End of file

    // Read temperature value until newline
    valueBuffer.Clear();
    while ((b = GetNextByte()) != 0 && b != (byte)'\n' && b != (byte)'\r')
    {
        valueBuffer.Add(b);
    }

    // Skip any additional newline characters
    if (b == (byte)'\r')
    {
        byte next = GetNextByte();
        if (next != (byte)'\n' && next != 0)
        {
            bufferPos--; // Put back if not \n
        }
    }

    if (nameBuffer.Count == 0) break; // End of file

    var name = nameBuffer.ToArray();
    
    // Parse value as int, skipping decimal point
    int value = 0;
    bool isNegative = false;
    foreach (byte bt in valueBuffer)
    {
        if (bt == (byte)'-')
        {
            isNegative = true;
        }
        else if (bt != (byte)'.')
        {
            value = value * 10 + (bt - (byte)'0');
        }
    }
    if (isNegative) value = -value;

    if (hm.ContainsKey(name))
    {
        hm[name].Add(value);
    }
    else
    {
        var tmp = new Measurement();
        tmp.Add(value);
        hm.Add(name, tmp);
    }
}

foreach (var v in hm.OrderBy(o => Encoding.UTF8.GetString(o.Key)))
{
    Console.WriteLine($"{Encoding.UTF8.GetString(v.Key)};{v.Value.ToString()}");
}

stopwatch.Stop();
TimeSpan elapsed = stopwatch.Elapsed;
string formatTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            elapsed.Hours, elapsed.Minutes, elapsed.Seconds,
            elapsed.Milliseconds / 10);

Console.WriteLine($"RunTime: {formatTime}");
Console.WriteLine($"Elapsed milliseconds: {stopwatch.ElapsedMilliseconds}");

internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (x == null || y == null) return x == y;
        if (x.Length != y.Length) return false;
        for (int i = 0; i < x.Length; i++)
        {
            if (x[i] != y[i]) return false;
        }
        return true;
    }

    public int GetHashCode(byte[] obj)
    {
        if (obj == null) return 0;
        int hash = 17;
        foreach (byte b in obj)
        {
            hash = hash * 31 + b;
        }
        return hash;
    }
}

internal sealed class Measurement
{
    private int _count = 0;
    private long _sum = 0;
    private int _min = int.MaxValue;
    private int _max = int.MinValue;

    public void Add(int value)
    {
        _count++;
        _sum += value;
        if (_min > value) _min = value;
        if (_max < value) _max = value;
    }

    public override string ToString()
    {
        double min = _min / 10.0;
        double avg = (_sum / (double)_count) / 10.0;
        double max = _max / 10.0;
        return $"{min:F1};{avg:F1};{max:F1}";
    }
}