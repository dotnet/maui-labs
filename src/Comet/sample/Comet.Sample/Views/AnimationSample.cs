

using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class AnimationSample : Component
	{
		public AnimationSample()
		{
			
		}
		readonly Reactive<bool> shouldAnimate = true;
		Text animatedText;
		Button button;
		public override View Render() =>
			VStack(
				Text("Regular Text Behind..."),
				(animatedText = Text("Text to Animate!")
					.Background(Colors.Orange)
					.Color(Colors.Blue)
					.FontSize(10)
					.Animate(duration: 3,repeats:true, autoReverses:true, action: (text) => {
						text.Background(Colors.Blue)
							.Color(Colors.Orange);
						text.FontSize(30);
					})

					//new Animation
					//{
					//	Duration = 2000,
					//	Delay = 500,
					//	Options = AnimationOptions.CurveEaseOut | AnimationOptions.Repeat,
					//	TranslateTo = new PointF(100, 50),
					//	RotateTo = 30,
					//	ScaleTo = new PointF(2f, 2f),
					//}
					),
				Text("Regular Text Above...").Background(Colors.White)
				.BeginAnimationSequence(repeats:true)
					.Animate(duration:1,action:(text)=>{
						text.Background(Colors.Fuchsia);
						button.Background(Colors.Green);
					}).Animate(duration:1,action:(text)=>{
						text.Background(Colors.AliceBlue);
					}).Animate(duration:1,action:(text)=>{
						text.Background(Colors.Beige);
						button.Background(Colors.Blue);
					}).Animate(duration:1,action:(text)=>{
						text.Background(Colors.BlueViolet);
					}).Animate(duration:1,action:(text)=>{
						text.Background(Colors.Lavender);
					}).Animate(duration:1,action:(text)=>{
						text.Background(Colors.Fuchsia);
						button.Background(Colors.Green);
				}).EndAnimationSequence(),
				Text("Does this move?").Background(Colors.LightBlue).Frame(width:100,height:44).Animate((text)=>{
					text.Frame(width:400, height: 100);
				},duration:3,repeats:true,autoReverses:true),
				(button = Button("Animate", () => {
					shouldAnimate.Value = !shouldAnimate;
					if(shouldAnimate)
						this.ResumeAnimations();
					else
						this.PauseAnimations();
				}).Background(Colors.Transparent))
			);
	}
}
