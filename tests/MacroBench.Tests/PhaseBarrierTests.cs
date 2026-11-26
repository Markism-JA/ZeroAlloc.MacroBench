using MacroBench.Orchestration;

namespace MacroBench.Tests;

public class PhaseBarrierTests
{
    [Fact]
    public void SignalAndWait_MultiThreadMultiGeneration_NeverDropsWakeups()
    {
        const int participants = 4;
        const int generations = 250;

        using var barrier = new PhaseBarrier(participants);
        int[] counters = new int[participants];
        Exception? failure = null;

        var threads = new Thread[participants];
        for (int i = 0; i < participants; i++)
        {
            int idx = i;
            threads[i] = new Thread(() =>
            {
                try
                {
                    for (int g = 0; g < generations; g++)
                    {
                        counters[idx]++;
                        barrier.SignalAndWait(idx);

                        for (int j = 0; j < participants; j++)
                        {
                            Assert.True(Volatile.Read(ref counters[j]) >= g + 1);
                        }

                        barrier.SignalAndWait(idx);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref failure, ex, null);
                }
            });
            threads[i].Start();
        }

        foreach (Thread t in threads)
        {
            Assert.True(t.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Null(failure);
        Assert.All(counters, c => Assert.Equal(generations, c));
    }

    [Fact]
    public void SignalAndWait_SlowArrivalAfterParkThreshold_WakesCleanly()
    {
        using var barrier = new PhaseBarrier(2);
        bool followerExited = false;

        var follower = new Thread(() =>
        {
            barrier.SignalAndWait(0);
            followerExited = true;
        });
        follower.Start();

        // Give follower enough time to exceed the 2048 spin threshold and enter ThreadParker.Park
        Thread.Sleep(25);
        barrier.SignalAndWait(1);

        Assert.True(follower.Join(TimeSpan.FromSeconds(5)));
        Assert.True(followerExited);
    }
}
