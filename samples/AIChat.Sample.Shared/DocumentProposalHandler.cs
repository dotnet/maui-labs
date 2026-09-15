using System.Text.Json;
using AIChat.ClientServer.Sample.Shared;
using Microsoft.Extensions.AI;
using Microsoft.Maui.AI.Chat;

namespace AIChat.Sample.Shared;

/// <summary>Stages a predictive document proposal before its manual UI action is invoked.</summary>
public sealed class DocumentProposalHandler(SampleUiState state)
    : ContentBlockHandler<DocumentProposalHandler.State>
{
    public override BlockMappingResult<State> Handle(BlockMappingContext context, State handlerState)
    {
        foreach (var candidate in context.UnhandledContents)
        {
            if (candidate is not FunctionCallContent content ||
                !string.Equals(content.Name, "propose_document", StringComparison.Ordinal) ||
                content.Arguments is null ||
                !(content.Arguments.TryGetValue("proposal", out var proposal)
                    || content.Arguments.TryGetValue("document", out proposal)))
            {
                continue;
            }

            if (state.TryApplyDocumentProposal(JsonSerializer.SerializeToElement(proposal)))
            {
                // The UI action handler must still see this call and create the block.
                // Deliberately do not mark it handled.
                return BlockMappingResult<State>.Pass();
            }
        }

        return BlockMappingResult<State>.Pass();
    }

    public sealed class State;
}
