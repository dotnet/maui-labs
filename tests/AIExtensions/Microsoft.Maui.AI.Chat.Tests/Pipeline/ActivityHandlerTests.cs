// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Microsoft.Maui.AI.Chat.Tests.Pipeline;

public class ActivityHandlerTests
{
    [Fact]
    public async Task ActivityHandler_RawSnapshotAndCorrelatedDeltas_UpdateAndComplete()
    {
        var handler = new TestActivityHandler();
        var options = new UIAgentOptions();
        options.AddBlockHandler(handler);
        var pipeline = new BlockMappingPipeline(options);
        var changes = 0;

        var emitted = await ToListAsync(pipeline.Process(Update("activity-1", "started")));
        var block = Assert.IsType<TestActivityBlock>(Assert.Single(emitted));
        block.OnChanged(() => changes++);

        Assert.Equal("started", block.Value);
        Assert.Equal("activity-1", block.Id);

        Assert.Empty(await ToListAsync(pipeline.Process(Update("other", "ignored"))));
        Assert.Equal("started", block.Value);
        Assert.Equal(0, changes);

        Assert.Empty(await ToListAsync(pipeline.Process(Update("activity-1", "running"))));
        Assert.Equal("running", block.Value);
        Assert.Equal(BlockLifecycleState.Active, block.LifecycleState);
        Assert.Equal(1, changes);

        Assert.Empty(await ToListAsync(pipeline.Process(Update("activity-1", "finished"))));
        Assert.Equal("finished", block.Value);
        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
        Assert.Equal(2, changes);

        Assert.Equal(["started", "running", "finished"], handler.UpdatedValues);
    }

    [Fact]
    public async Task ActivityHandler_Finalize_DeactivatesActiveBlock()
    {
        var options = new UIAgentOptions();
        options.AddBlockHandler(new TestActivityHandler());
        var pipeline = new BlockMappingPipeline(options);

        var block = Assert.IsType<TestActivityBlock>(
            Assert.Single(await ToListAsync(pipeline.Process(Update("activity-1", "started")))));
        pipeline.Finalize();

        Assert.Equal(BlockLifecycleState.Inactive, block.LifecycleState);
    }

    private static ChatResponseUpdate Update(string id, string value) => new()
    {
        MessageId = id,
        RawRepresentation = JsonSerializer.SerializeToElement(new { id, value }),
    };

    private static async Task<List<ContentBlock>> ToListAsync(IAsyncEnumerable<ContentBlock> source)
    {
        var blocks = new List<ContentBlock>();
        await foreach (var block in source)
            blocks.Add(block);
        return blocks;
    }

    private sealed class TestActivityBlock : ActivityContentBlock
    {
        public string Value { get; set; } = string.Empty;
    }

    private sealed class TestActivityHandler : ActivityHandler<TestActivityBlock>
    {
        internal List<string> UpdatedValues { get; } = [];

        protected override bool TryCreateBlock(BlockMappingContext context, TestActivityBlock state)
        {
            if (!TryRead(context, out var id, out var value) || value != "started")
                return false;

            state.Id = id;
            state.Value = value;
            return true;
        }

        protected override bool TryUpdateBlock(
            BlockMappingContext context,
            TestActivityBlock state,
            out bool isCompleted)
        {
            isCompleted = false;
            if (!TryRead(context, out var id, out var value) || id != state.Id)
                return false;

            state.Value = value;
            isCompleted = value == "finished";
            return true;
        }

        protected override void OnContentUpdated(TestActivityBlock block)
        {
            UpdatedValues.Add(block.Value);
        }

        private static bool TryRead(
            BlockMappingContext context,
            out string id,
            out string value)
        {
            id = string.Empty;
            value = string.Empty;
            if (context.Update.RawRepresentation is not JsonElement raw
                || !raw.TryGetProperty("id", out var rawId)
                || !raw.TryGetProperty("value", out var rawValue))
            {
                return false;
            }

            id = rawId.GetString() ?? string.Empty;
            value = rawValue.GetString() ?? string.Empty;
            return true;
        }
    }
}
