namespace ControlGallery.Pages;

public class AlertPage : ContentPageBase
{
    Label YesNoLbl, ActionLbl, PromptLbl;
    Button AlertBtn, AlertResponseBtn, AlertActionBtn, AlertPromptBtn;

    protected override void Build()
    {
        this.Title = "Alerts";

        this.Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Children = {
                    new H1("Simple Alert"),
                    new Separator(),
                    new Button()
                        .Text("Display Alert")
                        .Assign(out AlertBtn),
                    new H1("Return Response"),
                    new Separator(),
                    new Button()
                        .Text("Display Alert")
                        .Assign(out AlertResponseBtn),

                    new Label()
                        .Assign(out YesNoLbl)
                        .TextColor(AppColors.Primary),

                    new H1("Action Sheet"),
                    new Separator(),
                    new Button()
                        .Text("Display Alert")
                        .Assign(out AlertActionBtn),
                    new Label()
                        .Assign(out ActionLbl)
                        .TextColor(AppColors.Primary),

                    new H1("Prompt"),
                    new Separator(),
                    new Button()
                        .Text("Display Alert")
                        .Assign(out AlertPromptBtn),
                    new Label()
                        .Assign(out PromptLbl)
                        .TextColor(AppColors.Primary),
                }
            }.DynamicResource(VisualElement.StyleProperty, "MainContainer")
        };

        AlertBtn.Clicked += (s, e) => TapAlert();
        AlertResponseBtn.Clicked += (s, e) => TapAlertResponse();
        AlertActionBtn.Clicked += (s, e) => TapAlertAction();
        AlertPromptBtn.Clicked += (s, e) => TapAlertPrompt();
    }

    private async void TapAlert()
    {
        await this.DisplayAlertAsync("Welcome", ".NET MAUI supports simple platform alerts.", "Okay");
    }

    private async void TapAlertResponse()
    {
        bool answer = await DisplayAlertAsync("Question?", "Would you like to play a game", "Yes", "No");
        this.Dispatcher.Dispatch(() =>
        {
            YesNoLbl.Text = "Answer: " + answer;
        });
    }

    private async void TapAlertAction()
    {
        string action = await DisplayActionSheetAsync("ActionSheet: Send to?", "Cancel", null, "Email", "Twitter", "Facebook");
        this.Dispatcher.Dispatch(() =>
        {
            ActionLbl.Text = "Action: " + action;
        });
    }

    private async void TapAlertPrompt()
    {
        string result = await DisplayPromptAsync("Question?", "What's your name?", "Ok", "Cancel", "Name", -1, Keyboard.Default, "Jon Doe");
        this.Dispatcher.Dispatch(() =>
        {
            PromptLbl.Text = "Result: " + result;
        });
    }
}