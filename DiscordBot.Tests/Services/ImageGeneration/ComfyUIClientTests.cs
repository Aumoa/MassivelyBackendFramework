using System.Text.Json.Nodes;
using DiscordBot.Services.ImageGeneration;

namespace DiscordBot.Tests.Services.ImageGeneration;

public sealed class ComfyUIClientTests
{
    [Fact]
    public void RandomizeSeeds_ReplacesSeedAndNoiseSeedLiteralValues()
    {
        var workflow = JsonNode.Parse("""
        {
            "1": { "inputs": { "seed": 111, "steps": 20 }, "class_type": "KSampler" },
            "2": { "inputs": { "noise_seed": 222 }, "class_type": "RandomNoise" }
        }
        """)!.AsObject();

        var values = new Queue<long>([1001, 1002]);
        ComfyUIClient.RandomizeSeeds(workflow, () => values.Dequeue());

        Assert.Equal(1001, workflow["1"]!["inputs"]!["seed"]!.GetValue<long>());
        Assert.Equal(1002, workflow["2"]!["inputs"]!["noise_seed"]!.GetValue<long>());
    }

    [Fact]
    public void RandomizeSeeds_DoesNotTouchOtherFields()
    {
        var workflow = JsonNode.Parse("""
        {
            "1": { "inputs": { "seed": 111, "steps": 20, "text": "hello" }, "class_type": "KSampler" }
        }
        """)!.AsObject();

        ComfyUIClient.RandomizeSeeds(workflow, () => 999);

        Assert.Equal(20, workflow["1"]!["inputs"]!["steps"]!.GetValue<int>());
        Assert.Equal("hello", workflow["1"]!["inputs"]!["text"]!.GetValue<string>());
    }

    [Fact]
    public void RandomizeSeeds_DoesNotTouchLinkedInputs()
    {
        var workflow = JsonNode.Parse("""
        {
            "1": { "inputs": { "seed": ["2", 0] }, "class_type": "KSampler" }
        }
        """)!.AsObject();

        var wasCalled = false;
        ComfyUIClient.RandomizeSeeds(workflow, () =>
        {
            wasCalled = true;
            return 999;
        });

        Assert.False(wasCalled);
        Assert.IsType<JsonArray>(workflow["1"]!["inputs"]!["seed"]);
    }
}
