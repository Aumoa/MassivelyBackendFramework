namespace NPay.Options;

public class SettlementAiSummaryOptions
{
    public bool Enabled { get; set; } = true;

    public string Model { get; set; } = "claude-sonnet-4-6";

    public float Temperature { get; set; } = 0.4f;

    public int MaxTokens { get; set; } = 160;
}
