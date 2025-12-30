using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Numerics;

CultureInfo newCulture = new("en-US");
Thread.CurrentThread.CurrentCulture = newCulture;

var stopwatch = new Stopwatch();
stopwatch.Start();

var filePath = Path.Combine("/Users/teis/code/1brc-files", "measurements-full.txt"); // measurements-million.txt

var fileInfo = new FileInfo(filePath);
long fileSize = fileInfo.Length;

int numThreads = Environment.ProcessorCount;
long chunkSize = fileSize / numThreads;

using var mmap = MemoryMappedFile.CreateFromFile(filePath, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);

var results = new ConcurrentDictionary<byte[], Measurement>(numThreads * 4, 500, new ByteArrayComparer());

Parallel.For(0, numThreads, threadIndex =>
{
    long startPos = threadIndex * chunkSize;
    long endPos = (threadIndex == numThreads - 1) ? fileSize : (threadIndex + 1) * chunkSize;
    
    if (threadIndex > 0)
    {
        using var adjustStream = mmap.CreateViewStream(startPos, endPos - startPos, MemoryMappedFileAccess.Read);
        int b;
        while ((b = adjustStream.ReadByte()) != -1 && b != '\n')
        {
            startPos++;
        }
        startPos++;
    }
    
    if (startPos >= endPos) return;
    
    using var stream = mmap.CreateViewStream(startPos, endPos - startPos, MemoryMappedFileAccess.Read);
    var localResults = ProcessChunk(stream, endPos - startPos);
    
    foreach (var kvp in localResults)
    {
        results.AddOrUpdate(kvp.Key, kvp.Value, (key, existing) =>
        {
            existing.Merge(kvp.Value);
            return existing;
        });
    }
});

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

static unsafe Dictionary<byte[], Measurement> ProcessChunk(Stream stream, long maxBytes)
{
    var stationCache = new Dictionary<int, List<byte[]>>(512);
    var localDict = new Dictionary<byte[], Measurement>(512, new ByteArrayComparer());
    
    var buffer = new byte[1024 * 1024];
    int bufferPos = 0;
    int bufferEnd = 0;
    long totalRead = 0;

    while (totalRead < maxBytes)
    {
        if (bufferPos >= bufferEnd)
        {
            int toRead = (int)Math.Min(buffer.Length, maxBytes - totalRead);
            if (toRead <= 0) break;
            
            int bytesRead = stream.Read(buffer, 0, toRead);
            if (bytesRead == 0) break;
            totalRead += bytesRead;
            bufferPos = 0;
            bufferEnd = bytesRead;
        }

        int lineStart = bufferPos;
        
        int semicolonPos = FindDelimiter(buffer, bufferPos, bufferEnd, (byte)';');
        if (semicolonPos == -1) break;
        
        int nameLen = semicolonPos - bufferPos;
        if (nameLen == 0 || nameLen > 100) break;
        
        uint hash = 2166136261u;
        for (int i = bufferPos; i < semicolonPos; i++)
        {
            hash ^= buffer[i];
            hash *= 16777619u;
        }
        int hashCode = (int)hash;
        
        bufferPos = semicolonPos + 1;
        
        int value = 0;
        int sign = 1;
        
        if (bufferPos < bufferEnd && buffer[bufferPos] == (byte)'-')
        {
            sign = -1;
            bufferPos++;
        }
        
        if (bufferPos + 3 < bufferEnd)
        {
            byte b1 = buffer[bufferPos];
            byte b2 = buffer[bufferPos + 1];
            byte b3 = buffer[bufferPos + 2];
            
            if (b2 == (byte)'.')
            {
                value = (b1 - (byte)'0') * 10 + (b3 - (byte)'0');
                bufferPos += 3;
            }
            else if (bufferPos + 4 < bufferEnd && buffer[bufferPos + 2] == (byte)'.')
            {
                value = (b1 - (byte)'0') * 100 + (b2 - (byte)'0') * 10 + (b3 - (byte)'0');
                bufferPos += 4;
            }
        }
        
        value *= sign;
        
        while (bufferPos < bufferEnd && buffer[bufferPos] != (byte)'\n')
        {
            bufferPos++;
        }
        bufferPos++;
        
        byte[]? stationKey = null;
        
        if (stationCache.TryGetValue(hashCode, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Length == nameLen)
                {
                    bool match = true;
                    for (int i = 0; i < nameLen; i++)
                    {
                        if (candidate[i] != buffer[lineStart + i])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        stationKey = candidate;
                        break;
                    }
                }
            }
        }
        
        if (stationKey == null)
        {
            stationKey = new byte[nameLen];
            Array.Copy(buffer, lineStart, stationKey, 0, nameLen);
            
            if (!stationCache.ContainsKey(hashCode))
            {
                stationCache[hashCode] = new List<byte[]>();
            }
            stationCache[hashCode].Add(stationKey);
            
            var measurement = new Measurement();
            measurement.Add(value);
            localDict[stationKey] = measurement;
        }
        else
        {
            localDict[stationKey].Add(value);
        }
    }
    
    return localDict;
}

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static int FindDelimiter(byte[] buffer, int start, int end, byte delimiter)
{
    int pos = start;
    
    if (Vector.IsHardwareAccelerated && (end - pos) >= Vector<byte>.Count)
    {
        var delimiterVec = new Vector<byte>(delimiter);
        
        while (pos + Vector<byte>.Count <= end)
        {
            var dataVec = new Vector<byte>(buffer, pos);
            if (Vector.EqualsAny(dataVec, delimiterVec))
            {
                for (int i = 0; i < Vector<byte>.Count && pos + i < end; i++)
                {
                    if (buffer[pos + i] == delimiter)
                        return pos + i;
                }
            }
            pos += Vector<byte>.Count;
        }
    }
    
    while (pos < end)
    {
        if (buffer[pos] == delimiter)
            return pos;
        pos++;
    }
    
    return -1;
}

internal sealed class ByteArrayComparer : IEqualityComparer<byte[]>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(byte[]? x, byte[]? y)
    {
        if (x == null || y == null) return x == y;
        if (x.Length != y.Length) return false;
        
        return x.AsSpan().SequenceEqual(y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetHashCode(byte[] obj)
    {
        if (obj == null) return 0;
        
        uint hash = 2166136261u;
        int i = 0;
        
        for (; i + 3 < obj.Length; i += 4)
        {
            hash ^= obj[i];
            hash *= 16777619u;
            hash ^= obj[i + 1];
            hash *= 16777619u;
            hash ^= obj[i + 2];
            hash *= 16777619u;
            hash ^= obj[i + 3];
            hash *= 16777619u;
        }
        
        for (; i < obj.Length; i++)
        {
            hash ^= obj[i];
            hash *= 16777619u;
        }
        
        return (int)hash;
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