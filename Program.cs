using System.Diagnostics;
using System.Dynamic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

CultureInfo newCulture = new("en-US");
Thread.CurrentThread.CurrentCulture = newCulture;

var stopwatch = new Stopwatch();
stopwatch.Start();

var filePath = Path.Combine("/Users/teis/code/1brc-files", "measurements-full.txt"); // measurements-million.txt

var fileInfo = new FileInfo(filePath);
long fileSize = fileInfo.Length;

// Determine number of chunks based on CPU cores
int numThreads = Environment.ProcessorCount;
long chunkSize = fileSize / numThreads;

var results = new ConcurrentDictionary<byte[], Measurement>(new ByteArrayComparer());

// Process chunks in parallel
Parallel.For(0, numThreads, threadIndex =>
{
    using var mmap = MemoryMappedFile.CreateFromFile(filePath);
    
    long startPos = threadIndex * chunkSize;
    long endPos = (threadIndex == numThreads - 1) ? fileSize : (threadIndex + 1) * chunkSize;
    
    // Adjust start position to next newline (except for first chunk)
    if (threadIndex > 0)
    {
        using var adjustStream = mmap.CreateViewStream(startPos, endPos - startPos, MemoryMappedFileAccess.Read);
        int b;
        while ((b = adjustStream.ReadByte()) != -1 && b != '\n')
        {
            startPos++;
        }
        startPos++; // Skip the newline
    }
    
    if (startPos >= endPos) return;
    
    using var stream = mmap.CreateViewStream(startPos, endPos - startPos, MemoryMappedFileAccess.Read);
    var localResults = ProcessChunk(stream, endPos - startPos);
    
    // Merge results into concurrent dictionary
    foreach (var kvp in localResults)
    {
        results.AddOrUpdate(kvp.Key, kvp.Value, (key, existing) =>
        {
            existing.Merge(kvp.Value);
            return existing;
        });
    }
});

// Sort and output
foreach (var v in results.OrderBy(o => o.Key, new ByteArrayLexicographicComparer()))
{
    Console.WriteLine($"{Encoding.UTF8.GetString(v.Key)};{v.Value}");
}

stopwatch.Stop();
TimeSpan elapsed = stopwatch.Elapsed;
string formatTime = String.Format("{0:00}:{1:00}:{2:00}.{3:00}",
            elapsed.Hours, elapsed.Minutes, elapsed.Seconds,
            elapsed.Milliseconds / 10);

Console.WriteLine($"RunTime: {formatTime}");
Console.WriteLine($"Elapsed milliseconds: {stopwatch.ElapsedMilliseconds}");

static Dictionary<byte[], Measurement> ProcessChunk(Stream stream, long maxBytes)
{
    var localDict = new Dictionary<byte[], Measurement>(new ByteArrayComparer());
    
    var nameBuffer = new byte[100];
    var valueBuffer = new byte[10];
    var buffer = new byte[256 * 1024];
    int bytesRead;
    int bufferPos = 0;
    int bufferEnd = 0;
    long totalRead = 0;

    byte GetNextByte()
    {
        if (bufferPos >= bufferEnd)
        {
            int toRead = (int)Math.Min(buffer.Length, maxBytes - totalRead);
            if (toRead <= 0) return 0;
            
            bytesRead = stream.Read(buffer, 0, toRead);
            if (bytesRead == 0) return 0;
            totalRead += bytesRead;
            bufferPos = 0;
            bufferEnd = bytesRead;
        }
        return buffer[bufferPos++];
    }

    while (totalRead < maxBytes)
    {
        // Read station name until ';'
        int nameLen = 0;
        byte b;
        while ((b = GetNextByte()) != 0 && b != (byte)';')
        {
            if (nameLen >= nameBuffer.Length) break;
            nameBuffer[nameLen++] = b;
        }

        if (b == 0) break;

        // Read temperature value until newline
        int valueLen = 0;
        while ((b = GetNextByte()) != 0 && b != (byte)'\n' && b != (byte)'\r')
        {
            if (valueLen >= valueBuffer.Length) break;
            valueBuffer[valueLen++] = b;
        }

        // Skip any additional newline characters
        if (b == (byte)'\r')
        {
            byte next = GetNextByte();
            if (next != (byte)'\n' && next != 0)
            {
                bufferPos--;
            }
        }

        if (nameLen == 0) break;

        // Create name array
        var name = new byte[nameLen];
        Array.Copy(nameBuffer, 0, name, 0, nameLen);
        
        // Parse value with optimized approach
        int value = ParseTemperature(valueBuffer, valueLen);

        if (!localDict.TryGetValue(name, out var measurement))
        {
            measurement = new Measurement();
            localDict.Add(name, measurement);
        }
        measurement.Add(value);
    }
    
    return localDict;
}

static int ParseTemperature(byte[] buffer, int length)
{
    // Fast path for common cases using SIMD-friendly approach
    int value = 0;
    bool isNegative = false;
    int startIdx = 0;
    
    if (length > 0 && buffer[0] == (byte)'-')
    {
        isNegative = true;
        startIdx = 1;
    }
    
    // Unrolled loop for better performance
    // Common formats: X.X (3 chars), XX.X (4 chars), -X.X (4 chars), -XX.X (5 chars)
    for (int i = startIdx; i < length; i++)
    {
        byte bt = buffer[i];
        if (bt != (byte)'.')
        {
            value = value * 10 + (bt - (byte)'0');
        }
    }
    
    return isNegative ? -value : value;
}

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

internal sealed class ByteArrayLexicographicComparer : IComparer<byte[]>
{
    public int Compare(byte[]? x, byte[]? y)
    {
        if (x == null) return y == null ? 0 : -1;
        if (y == null) return 1;
        
        int minLen = Math.Min(x.Length, y.Length);
        for (int i = 0; i < minLen; i++)
        {
            if (x[i] != y[i]) return x[i].CompareTo(y[i]);
        }
        return x.Length.CompareTo(y.Length);
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

    public void Merge(Measurement other)
    {
        _count += other._count;
        _sum += other._sum;
        if (_min > other._min) _min = other._min;
        if (_max < other._max) _max = other._max;
    }

    public override string ToString()
    {
        double min = _min / 10.0;
        double avg = (_sum / (double)_count) / 10.0;
        double max = _max / 10.0;
        return $"{min:F1};{avg:F1};{max:F1}";
    }
}