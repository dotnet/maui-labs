#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace CometSamples.Reply
{
	/// <summary>Straight port of the gold's data layer (data/Account.kt, Email.kt,
	/// local/Local*DataProvider.kt): same ids, names, avatars, subjects, bodies and
	/// timestamps, so list rows and detail threads match the gold pixel content.
	/// Deterministic: the gold shuffles each email's threads; here they keep source
	/// order (a stable fixture for the smoke scripts).</summary>
	public sealed record ReplyAccount(long Id, long Uid, string FirstName, string LastName, string Email, string Avatar)
	{
		public string FullName => $"{FirstName} {LastName}";
	}

	public enum ReplyMailbox { Inbox, Sent, Drafts, Spam, Trash }

	public sealed record ReplyEmail(
		long Id, ReplyAccount Sender, string Subject, string Body, string CreatedAt,
		ReplyMailbox Mailbox = ReplyMailbox.Inbox, bool IsStarred = false, bool IsImportant = false,
		IReadOnlyList<ReplyEmail>? Threads = null);

	public static class ReplyData
	{
		// local/LocalAccountsDataProvider.kt — contact accounts (id, uid, names, avatar).
		public static readonly ReplyAccount User = new(1, 0, "Jeff", "Hansen", "hikingfan@gmail.com", "avatar_10");
		static readonly Dictionary<long, ReplyAccount> Contacts = new[]
		{
			new ReplyAccount(4, 1, "Tracy", "Alvarez", "tracealvie@gmail.com", "avatar_1"),
			new ReplyAccount(5, 2, "Allison", "Trabucco", "atrabucco222@gmail.com", "avatar_3"),
			new ReplyAccount(6, 3, "Ali", "Connors", "aliconnors@gmail.com", "avatar_5"),
			new ReplyAccount(7, 4, "Alberto", "Williams", "albertowilliams124@gmail.com", "avatar_0"),
			new ReplyAccount(8, 5, "Kim", "Alen", "alen13@gmail.com", "avatar_7"),
			new ReplyAccount(9, 6, "Google", "Express", "express@google.com", "avatar_express"),
			new ReplyAccount(10, 7, "Sandra", "Adams", "sandraadams@gmail.com", "avatar_2"),
			new ReplyAccount(11, 8, "Trevor", "Hansen", "trevorhandsen@gmail.com", "avatar_8"),
			new ReplyAccount(12, 9, "Sean", "Holt", "sholt@gmail.com", "avatar_6"),
			new ReplyAccount(13, 10, "Frank", "Hawkins", "fhawkank@gmail.com", "avatar_4"),
		}.ToDictionary(a => a.Id);

		static ReplyAccount C(long id) => Contacts[id];

		// local/LocalEmailsDataProvider.kt `threads` — the conversation shown in every detail.
		public static readonly IReadOnlyList<ReplyEmail> Threads = new[]
		{
			new ReplyEmail(8, C(13), "Your update on Google Play Store is live!",
				"Your update, 0.1.1, is now live on the Play Store and available for your alpha users to start testing.\n\nYour alpha testers will be automatically notified. If you'd rather send them a link directly, go to your Google Play Console and follow the instructions for obtaining an open alpha testing link.",
				"3 hours ago", ReplyMailbox.Trash),
			new ReplyEmail(5, C(13), "Update to Your Itinerary", "", "2 hours ago"),
			new ReplyEmail(6, C(10), "Recipe to try",
				"Raspberry Pie: We should make this pie recipe tonight! The filling is very quick to put together.",
				"2 hours ago", ReplyMailbox.Sent),
			new ReplyEmail(7, C(9), "Delivered", "Your shoes should be waiting for you at home!", "2 hours ago"),
			new ReplyEmail(9, C(10), "(No subject)", "Hey, \n\nWanted to email and see what you thought of", "3 hours ago", ReplyMailbox.Drafts),
			new ReplyEmail(1, C(6), "Brunch this weekend?",
				"I'll be in your neighborhood doing errands and was hoping to catch you for a coffee this Saturday. If you don't have anything scheduled, it would be great to see you! It feels like its been forever.\n\nIf we do get a chance to get together, remind me to tell you about Kim. She stopped over at the house to say hey to the kids and told me all about her trip to Mexico.\n\nTalk to you soon,\n\nAli",
				"40 mins ago"),
			new ReplyEmail(2, C(5), "Bonjour from Paris", "Here are some great shots from my trip...", "1 hour ago", IsImportant: true),
		};

		// local/LocalEmailsDataProvider.kt `allEmails` — source order = gold list order.
		public static readonly IReadOnlyList<ReplyEmail> AllEmails = new[]
		{
			new ReplyEmail(0, C(9), "Package shipped!",
				"Cucumber Mask Facial has shipped.\n\nKeep an eye out for a package to arrive between this Thursday and next Tuesday. If for any reason you don't receive your package before the end of next week, please reach out to us for details on your shipment.\n\nAs always, thank you for shopping with us and we hope you love our specially formulated Cucumber Mask!",
				"20 mins ago", IsStarred: true, Threads: Threads),
			new ReplyEmail(1, C(6), "Brunch this weekend?",
				"I'll be in your neighborhood doing errands and was hoping to catch you for a coffee this Saturday. If you don't have anything scheduled, it would be great to see you! It feels like its been forever.\n\nIf we do get a chance to get together, remind me to tell you about Kim. She stopped over at the house to say hey to the kids and told me all about her trip to Mexico.\n\nTalk to you soon,\n\nAli",
				"40 mins ago", Threads: Threads),
			new ReplyEmail(2, C(5), "Bonjour from Paris", "Here are some great shots from my trip...",
				"1 hour ago", IsImportant: true, Threads: Threads),
			new ReplyEmail(3, C(8), "High school reunion?",
				"Hi friends,\n\nI was at the grocery store on Sunday night.. when I ran into Genie Williams! I almost didn't recognize her afer 20 years!\n\nAnyway, it turns out she is on the organizing committee for the high school reunion this fall. I don't know if you were planning on going or not, but she could definitely use our help in trying to track down lots of missing alums. If you can make it, we're doing a little phone-tree party at her place next Saturday, hoping that if we can find one person, thee more will...",
				"2 hours ago", ReplyMailbox.Sent, Threads: Threads),
			new ReplyEmail(4, C(11), "Brazil trip",
				"Thought we might be able to go over some details about our upcoming vacation.\n\nI've been doing a bit of research and have come across a few paces in Northern Brazil that I think we should check out. One, the north has some of the most predictable wind on the planet. I'd love to get out on the ocean and kitesurf for a couple of days if we're going to be anywhere near or around Taiba. I hear it's beautiful there and if you're up for it, I'd love to go. Other than that, I haven't spent too much time looking into places along our road trip route. I'm assuming we can find places to stay and things to do as we drive and find places we think look interesting. But... I know you're more of a planner, so if you have ideas or places in mind, lets jot some ideas down!\n\nMaybe we can jump on the phone later today if you have a second.",
				"2 hours ago", IsStarred: true, Threads: Threads),
			new ReplyEmail(5, C(13), "Update to Your Itinerary", "", "2 hours ago", Threads: Threads),
			new ReplyEmail(6, C(10), "Recipe to try",
				"Raspberry Pie: We should make this pie recipe tonight! The filling is very quick to put together.",
				"2 hours ago", ReplyMailbox.Sent, Threads: Threads),
			new ReplyEmail(7, C(9), "Delivered", "Your shoes should be waiting for you at home!",
				"2 hours ago", Threads: Threads),
			new ReplyEmail(8, C(13), "Your update on Google Play Store is live!",
				"Your update, 0.1.1, is now live on the Play Store and available for your alpha users to start testing.\n\nYour alpha testers will be automatically notified. If you'd rather send them a link directly, go to your Google Play Console and follow the instructions for obtaining an open alpha testing link.",
				"3 hours ago", ReplyMailbox.Trash, Threads: Threads),
			new ReplyEmail(9, C(10), "(No subject)", "Hey, \n\nWanted to email and see what you thought of",
				"3 hours ago", ReplyMailbox.Drafts, Threads: Threads),
			new ReplyEmail(10, C(5), "Try a free TrailGo account",
				"Looking for the best hiking trails in your area? TrailGo gets you on the path to the outdoors faster than you can pack a sandwich. \n\nWhether you're an experienced hiker or just looking to get outside for the afternoon, there's a segment that suits you.",
				"3 hours ago", ReplyMailbox.Trash, Threads: Threads),
			new ReplyEmail(11, C(5), "Free money",
				"You've been selected as a winner in our latest raffle! To claim your prize, click on the link.",
				"3 hours ago", ReplyMailbox.Spam, Threads: Threads),
		};
	}
}
