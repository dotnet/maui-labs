using System;
using System.Collections.Generic;

using System.Threading.Tasks;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace Comet.Samples
{
	public class AuditReportPage : Component
	{
		readonly Reactive<List<ApiAuditManager.AuditReport>> reports = new List<ApiAuditManager.AuditReport>();
		readonly Reactive<bool> isLoading = false;

				public override View Render()
		{
			//if (isLoading) return Text(() => "Loading...");
			if (isLoading) return ActivityIndicator().Color(Colors.Fuchsia);

			if (reports.Value.Count == 0) return Button(() => "Generate Report", async () => {
				isLoading.Value = true;
				try
				{
					var items = await Task.Run(() => ApiAuditManager.GenerateReport());
					reports.Value = items;
				}
				finally
				{
					isLoading.Value = false;
				}
			});

			return new ListView<ApiAuditManager.AuditReport>(reports.Value)
			{
				ViewFor = (report) => HStack(
						VStack(LayoutAlignment.Start,
							Text(report.View).FontSize(20),
							Text($"Handler: {report.Handler}"),
							Text($"Has Map? : {!report.MissingMapper}").Color(report.MissingMapper ? Colors.Red : Colors.Green),
							Text($"Handled Properties: {report.HandledProperties.Count}").Color(report.HandledProperties.Count == 0 ? Colors.Red : Colors.Green),
							Text($"Missing Count: {report.UnHandledProperties.Count}").Color(report.UnHandledProperties.Count == 0 ? Colors.Green : Colors.Red)
						).Margin().FontSize(10)
						.OnTapNavigate(()=>new AuditReportPageDetails().SetEnvironment("report", report))
				 ),
			};
			//.OnSelectedNavigate((report) => new AuditReportPageDetails().SetEnvironment("report", report)); ;
		}
	}
	public class AuditReportPageDetails : Component
	{
		[Environment]
		readonly ApiAuditManager.AuditReport report;
				public override View Render()
		{
			var stack = VStack(

			);//.Frame(alignment:Alignment.Top);
			if (report.HandledProperties.Count > 0)
			{
				stack.Add(Text("Handled Properties").FontSize(30));
				foreach (var prop in report.HandledProperties)
				{
					stack.Add(Text(prop).Color(Colors.Green));
				}
			}
			if (report.UnHandledProperties.Count > 0)
			{
				stack.Add(Text("UnHandled Properties!").FontSize(30));
				foreach (var prop in report.UnHandledProperties)
				{
					stack.Add(Text(prop).Color(Colors.Red));
				}
			}
			return stack;
		}
	}
}
