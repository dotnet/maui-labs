using Comet.Reactive;

namespace CometMailApp.Models;

/// <summary>
/// In-memory reactive email store. Replaces ReactorData/Shiny background services
/// with a simple SignalList for reactive UI updates.
/// </summary>
public static class MailStore
{
	public static readonly SignalList<EmailMessage> Inbox = new(SeedData());

	static List<EmailMessage> SeedData() => new()
	{
		new EmailMessage
		{
			Sender = "Alice Johnson",
			SenderEmail = "alice@example.com",
			Subject = "Project kickoff meeting",
			Preview = "Hi team, I'd like to schedule our project kickoff for next Monday...",
			Body = "Hi team,\n\nI'd like to schedule our project kickoff for next Monday at 10 AM. Please review the attached brief before the meeting and come prepared with questions.\n\nThe agenda will cover:\n• Project goals and timeline\n• Role assignments\n• Communication channels\n• First sprint planning\n\nLooking forward to getting started!\n\nBest,\nAlice",
			ReceivedAt = DateTime.Now.AddMinutes(-12),
			IsRead = false,
		},
		new EmailMessage
		{
			Sender = "Bob Martinez",
			SenderEmail = "bob.martinez@example.com",
			Subject = "Code review request: PR #247",
			Preview = "Hey, I just opened a pull request for the auth module refactoring...",
			Body = "Hey,\n\nI just opened a pull request for the auth module refactoring we discussed last week. It touches about 15 files but the core change is straightforward — moving from cookie-based sessions to JWT tokens.\n\nKey changes:\n• New JwtTokenService with refresh token rotation\n• Updated middleware pipeline\n• Migration script for existing sessions\n• 23 new unit tests\n\nWould appreciate your review when you get a chance. No rush, but I'd like to merge before Thursday.\n\nThanks!\nBob",
			ReceivedAt = DateTime.Now.AddHours(-1),
			IsRead = false,
		},
		new EmailMessage
		{
			Sender = "Carol Chen",
			SenderEmail = "carol.chen@example.com",
			Subject = "Lunch tomorrow?",
			Preview = "Are you free for lunch tomorrow? There's a new Thai place...",
			Body = "Are you free for lunch tomorrow? There's a new Thai place that opened on 5th Street and I've been wanting to try it. They have great reviews for their pad see ew.\n\nLet me know if noon works for you!\n\nCarol",
			ReceivedAt = DateTime.Now.AddHours(-3),
			IsRead = true,
		},
		new EmailMessage
		{
			Sender = "David Park",
			SenderEmail = "d.park@example.com",
			Subject = "Re: Q4 budget proposal",
			Preview = "I've reviewed the numbers and have a few suggestions for the cloud infra line...",
			Body = "I've reviewed the numbers and have a few suggestions for the cloud infrastructure line item.\n\nThe current estimate of $45K/month seems high given our planned migration to ARM-based instances. I think we can bring that down to ~$32K by:\n\n1. Switching to Graviton3 instances (30% cost reduction)\n2. Using spot instances for CI/CD workloads\n3. Consolidating our dev/staging environments\n\nI've attached a revised spreadsheet with the updated projections. Happy to walk through it on our next sync.\n\nDavid",
			ReceivedAt = DateTime.Now.AddHours(-5),
			IsRead = true,
		},
		new EmailMessage
		{
			Sender = "GitHub Notifications",
			SenderEmail = "notifications@github.com",
			Subject = "[dotnet/maui] Issue #19832: ScrollView performance regression",
			Preview = "A new issue has been opened in dotnet/maui by user @scrollfan...",
			Body = "A new issue has been opened in dotnet/maui:\n\n#19832 — ScrollView performance regression on iOS 18\n\nOpened by @scrollfan\n\nDescription:\nAfter updating to .NET 10 Preview 4, ScrollView with large item counts (500+) shows significant jank on iOS 18. The same code runs smoothly on Android.\n\nSteps to reproduce:\n1. Create a ScrollView with 1000 Label items\n2. Scroll quickly through the list\n3. Observe frame drops on iOS\n\nExpected: Smooth 60fps scrolling\nActual: Drops to ~15fps during fast scroll\n\n—\nYou are receiving this because you are subscribed to this repository.",
			ReceivedAt = DateTime.Now.AddHours(-8),
			IsRead = false,
			IsStarred = true,
		},
		new EmailMessage
		{
			Sender = "Emma Wilson",
			SenderEmail = "emma.w@example.com",
			Subject = "Conference talk accepted!",
			Preview = "Great news — our talk on MVU patterns in .NET MAUI was accepted...",
			Body = "Great news — our talk on MVU patterns in .NET MAUI was accepted for DotNetConf 2025!\n\nThe session is scheduled for Day 2, Track B, 2:00 PM EST. We have a 45-minute slot plus 15 minutes for Q&A.\n\nNext steps:\n• Finalize the slide deck by Nov 1\n• Record a practice run by Nov 8\n• Submit final materials by Nov 15\n\nI've shared the slide template in our shared drive. Let me know which sections you'd like to take!\n\nExcited about this! \nEmma",
			ReceivedAt = DateTime.Now.AddDays(-1),
			IsRead = true,
			IsStarred = true,
		},
		new EmailMessage
		{
			Sender = "Frank Torres",
			SenderEmail = "frank.t@example.com",
			Subject = "Server migration this weekend",
			Preview = "Heads up — we're migrating the production database cluster this Saturday...",
			Body = "Heads up — we're migrating the production database cluster this Saturday starting at 2 AM UTC.\n\nExpected downtime: 30-45 minutes\n\nWhat you need to know:\n• All API endpoints will return 503 during migration\n• The status page will be updated in real-time\n• Rollback plan is tested and ready\n• New connection strings will be the same (DNS switch)\n\nPlease avoid deploying to production from Friday 6 PM to Sunday 6 AM.\n\nIf you have concerns, reply to this thread or ping me on Slack.\n\nFrank",
			ReceivedAt = DateTime.Now.AddDays(-1).AddHours(-4),
			IsRead = true,
		},
		new EmailMessage
		{
			Sender = "Grace Kim",
			SenderEmail = "grace.kim@example.com",
			Subject = "Design review: new onboarding flow",
			Preview = "I've uploaded the updated mockups for the onboarding redesign...",
			Body = "I've uploaded the updated mockups for the onboarding redesign to Figma.\n\nChanges from v2:\n• Simplified the account creation to 2 steps (was 4)\n• Added progress indicator\n• New illustration set for each step\n• Dark mode variants included\n• Accessibility audit passed — all contrast ratios meet WCAG AA\n\nFigma link: [redacted]\n\nPlease leave comments directly on the frames. I'd like to finalize by end of week so engineering can start next sprint.\n\nThanks,\nGrace",
			ReceivedAt = DateTime.Now.AddDays(-2),
			IsRead = false,
		},
	};

	public static void MarkAsRead(Guid messageId)
	{
		var msg = Inbox.FirstOrDefault(m => m.Id == messageId);
		if (msg != null)
		{
			msg.IsRead = true;
			Inbox.Batch(list => { });
		}
	}

	public static void ToggleStar(Guid messageId)
	{
		var msg = Inbox.FirstOrDefault(m => m.Id == messageId);
		if (msg != null)
		{
			msg.IsStarred = !msg.IsStarred;
			Inbox.Batch(list => { });
		}
	}

	public static void DeleteMessage(Guid messageId)
	{
		var msg = Inbox.FirstOrDefault(m => m.Id == messageId);
		if (msg != null)
			Inbox.Remove(msg);
	}

	public static void SendMessage(string to, string subject, string body)
	{
		Inbox.Insert(0, new EmailMessage
		{
			Sender = "You",
			SenderEmail = to,
			Subject = $"Sent: {subject}",
			Preview = body.Length > 80 ? body[..80] + "..." : body,
			Body = body,
			ReceivedAt = DateTime.Now,
			IsRead = true,
		});
	}
}
