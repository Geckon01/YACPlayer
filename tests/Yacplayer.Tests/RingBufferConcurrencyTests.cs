using Yacplayer.Audio;

namespace Yacplayer.Tests;

public class RingBufferConcurrencyTests
{
    [Fact]
    public void ProducerOverflowNeverRollsBackTheConsumerReadPosition()
    {
        // Regression: the producer used to write _read during overflow, which could
        // race a concurrent consumer update and rewind the read position.
        const int capacity = 1024;
        var ring = new SampleRingBuffer(capacity);
        var producer = new Thread(() =>
        {
            var block = new float[64];
            for (var iteration = 0; iteration < 40_000; iteration++)
            {
                ring.Write(block);
            }
        })
        { IsBackground = true };

        var failures = 0;
        var consumer = new Thread(() =>
        {
            var buffer = new float[257];
            for (var iteration = 0; iteration < 40_000; iteration++)
            {
                var read = ring.Read(buffer);
                if (read is < 0 || read > capacity)
                {
                    Interlocked.Increment(ref failures);
                }

                var count = ring.Count;
                if (count < 0 || count > capacity)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })
        { IsBackground = true };

        producer.Start();
        consumer.Start();
        Assert.True(producer.Join(TimeSpan.FromSeconds(30)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, Volatile.Read(ref failures));
        Assert.InRange(ring.Count, 0, capacity);
    }

    [Fact]
    public void ConcurrentReadLatestResetAndWriteKeepTheRingConsistent()
    {
        const int capacity = 512;
        var ring = new SampleRingBuffer(capacity);
        var failures = 0;
        var producer = new Thread(() =>
        {
            var block = new float[33];
            for (var iteration = 0; iteration < 30_000; iteration++)
            {
                ring.Write(block);
            }
        })
        { IsBackground = true };

        var consumer = new Thread(() =>
        {
            var latest = new float[96];
            var exact = new float[64];
            for (var iteration = 0; iteration < 30_000; iteration++)
            {
                var count = ring.ReadLatest(latest);
                if (count is < 0 || count > capacity)
                {
                    Interlocked.Increment(ref failures);
                }

                if (iteration % 97 == 0)
                {
                    ring.Reset();
                }

                if (ring.Count is < 0 || ring.Count > capacity)
                {
                    Interlocked.Increment(ref failures);
                }
            }
        })
        { IsBackground = true };

        producer.Start();
        consumer.Start();
        Assert.True(producer.Join(TimeSpan.FromSeconds(30)));
        Assert.True(consumer.Join(TimeSpan.FromSeconds(30)));
        Assert.Equal(0, Volatile.Read(ref failures));
    }

    [Fact]
    public void ReadNeverExposesMoreThanOneCapacityAfterConsumerFallsBehind()
    {
        var ring = new SampleRingBuffer(8);
        var values = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();
        ring.Write(values);

        // A stale read position far behind the producer must be clamped, not overrun.
        var buffer = new float[64];
        var read = ring.Read(buffer);
        Assert.InRange(read, 0, 8);
        Assert.Equal(Enumerable.Range(92, 8).Select(i => (float)i), buffer.Take(read));
        Assert.Equal(0, ring.Count);
    }
}
