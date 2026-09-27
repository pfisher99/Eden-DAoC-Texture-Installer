namespace SqeaksDaocTextures.Tests;

public sealed class ProgressMathTests
{
    [Theory]
    [InlineData(0, 0, 100)]
    [InlineData(0, 10, 0)]
    [InlineData(4, 10, 40)]
    [InlineData(10, 10, 100)]
    [InlineData(12, 10, 100)]
    public void Component_percentage_is_clamped_and_empty_folders_are_complete(
        long completed, long total, double expected)
    {
        Assert.Equal(expected, ProgressMath.ComponentPercent(completed, total));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 10, 0)]
    [InlineData(4, 10, 40)]
    [InlineData(10, 10, 99)]
    [InlineData(12, 10, 99)]
    public void Overall_work_percentage_reserves_completion_for_persisted_state(
        long completed, long total, double expected)
    {
        Assert.Equal(expected, ProgressMath.OverallWorkPercent(completed, total));
    }

    [Fact]
    public void Backup_to_extraction_transition_remains_monotonic()
    {
        var values = new[]
        {
            ProgressMath.OverallWorkPercent(0, 10),
            ProgressMath.OverallWorkPercent(4, 10),
            ProgressMath.OverallWorkPercent(5, 10),
            ProgressMath.OverallWorkPercent(9, 10),
            ProgressMath.OverallWorkPercent(10, 10),
            100
        };

        Assert.Equal(values.OrderBy(value => value), values);
        Assert.Equal(100, values[^1]);
    }
}
