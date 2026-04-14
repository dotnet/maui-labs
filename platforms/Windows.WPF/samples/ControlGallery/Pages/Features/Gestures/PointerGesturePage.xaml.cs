namespace ControlGallery.Pages;

public partial class PointerGesturePage : ContentPage
{
	public PointerGesturePage()
	{
		InitializeComponent();
	}

	void HoverBegan(object sender, PointerEventArgs e)
 		{
 			btn.Text = "Thanks for hovering me!";
 		}

 		void HoverEnded(object sender, PointerEventArgs e)
 		{
 			btn.Text = "Hover me again!";
 			positionLabel.Text = "Hover above label to reveal pointer position again";
 		}

 		void HoverMoved(object sender, PointerEventArgs e)
 		{
 			positionLabel.Text = $"Pointer position is at: {e.GetPosition((View)sender)}";
 		}
}