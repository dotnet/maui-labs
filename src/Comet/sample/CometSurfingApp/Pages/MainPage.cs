using System.Collections.Generic;
using Comet;
using CometSurfingApp.Models;
using CometSurfingApp.Services;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometSurfingApp.Pages
{
	/// <summary>
	/// Comet port of the MauiReactor SurfingApp.
	/// Original: https://github.com/nicolgit/mauireactor-samples/tree/main/SurfingApp
	/// Design:  https://github.com/jsuarezruiz/netmaui-surfing-app-challenge
	///
	/// Renders a social-style surf feed with a horizontal user avatar strip
	/// and a vertical list of post cards, each with an image overlay, user
	/// info, like count, and title.
	/// </summary>
	public class MainPage : View
	{
		[Body]
		View body()
		{
			var users = UserService.Instance.GetUsers();
			var posts = PostService.Instance.GetPosts();

			return new Grid(
				rows: new object[] { 48, 68, "*" },
				columns: new object[] { "*" })
			{
				BuildHeader()
					.Cell(row: 0, column: 0),

				BuildUserStrip(users)
					.Cell(row: 1, column: 0),

				BuildPostFeed(posts)
					.Cell(row: 2, column: 0),
			}
			.Padding(new Thickness(0, 58, 0, 0));
		}

		// ── Header ──────────────────────────────────────────────────
		View BuildHeader()
		{
			return new Grid(
				rows: new object[] { "*" },
				columns: new object[] { "Auto", "*", "Auto" })
			{
				Text("Menu")
					.FontSize(22)
					.Color(Colors.Black)
					.Margin(new Thickness(24, 6))
					.VerticalTextAlignment(TextAlignment.Center)
					.Cell(row: 0, column: 0),

				Text("Surfers")
					.FontSize(28)
					.FontWeight(FontWeight.Bold)
					.Color(Colors.Black)
					.HorizontalTextAlignment(TextAlignment.Center)
					.VerticalTextAlignment(TextAlignment.Center)
					.Cell(row: 0, column: 1),

				Text("Search")
					.FontSize(20)
					.Margin(new Thickness(24, 6))
					.VerticalTextAlignment(TextAlignment.Center)
					.Cell(row: 0, column: 2),
			};
		}

		// ── Horizontal user avatar strip ────────────────────────────
		View BuildUserStrip(List<User> users)
		{
			var hstack = new HStack(spacing: 6);
			foreach (var user in users)
			{
				hstack.Add(BuildUserAvatar(user));
			}
			return ScrollView(Orientation.Horizontal, hstack)
				.Padding(new Thickness(24, 0))
				.Margin(new Thickness(0, 4));
		}

		View BuildUserAvatar(User user)
		{
			return Image(user.Image)
				.Frame(width: 56, height: 56)
				.ClipShape(new Ellipse())
				.Aspect(Aspect.AspectFill)
				.RoundedBorder(radius: 28, color: user.Color, strokeSize: 4);
		}

		// ── Vertical post feed ──────────────────────────────────────
		View BuildPostFeed(List<Post> posts)
		{
			var vstack = new VStack(spacing: 16);
			foreach (var post in posts)
			{
				vstack.Add(BuildPostCard(post));
			}
			return ScrollView(Orientation.Vertical,
				vstack.FillHorizontal())
				.Padding(new Thickness(24, 4, 14, 0));
		}

		// ── Individual post card ────────────────────────────────────
		// Flattened from 7 nesting levels to 4 to prevent Android stack overflow.
		// All VStack/HStack containers replaced with Grid cell positioning.
		View BuildPostCard(Post post)
		{
			return new ZStack
			{
				// Background image fills the card
				Image(post.Image)
					.Aspect(Aspect.AspectFill)
					.FillHorizontal()
					.FillVertical(),

				// Dark overlay for contrast
				new Spacer()
					.Background(Colors.Black)
					.Opacity(0.1f),

				// Content overlay -- flat Grid, no nested stacks
				new Grid(
					rows: new object[] { "Auto", "Auto", "Auto", "*", 44 },
					columns: new object[] { 72, "*" })
				{
					// Row 0: User avatar
					Image(post.User.Image)
						.Frame(width: 54, height: 54)
						.Aspect(Aspect.AspectFill)
						.ClipShape(new Ellipse())
						.RoundedBorder(radius: 27, color: post.User.Color, strokeSize: 4)
						.Margin(new Thickness(12))
						.Cell(row: 0, column: 0)
						.GridRowSpan(2),

					// Row 0: User name
					Text(post.User.Name.ToUpper())
						.FontWeight(FontWeight.Bold)
						.Color(Colors.Black)
						.Margin(new Thickness(0, 18, 0, 0))
						.Cell(row: 0, column: 1),

					// Row 1: Time
					Text("4 HOURS AGO")
						.FontSize(10)
						.Color(Colors.Black)
						.Opacity(0.75f)
						.Cell(row: 1, column: 1),

					// Row 2: Likes + Save
					Text($"Likes  {post.Likes}")
						.FontSize(14)
						.Color(Colors.Black)
						.Margin(new Thickness(12, 4))
						.Cell(row: 2, column: 0)
						.GridColumnSpan(2),

					// Row 3: Spacer
					new Spacer()
						.Cell(row: 3, column: 0)
						.GridColumnSpan(2),

					// Row 4: Play button
					Text("Play")
						.FontSize(16)
						.Color(Colors.Black)
						.HorizontalTextAlignment(TextAlignment.Center)
						.VerticalTextAlignment(TextAlignment.Center)
						.Frame(width: 44, height: 44)
						.ClipShape(new Ellipse())
						.Background(Colors.White)
						.Margin(new Thickness(12, 0))
						.Cell(row: 4, column: 0),

					// Row 4: Title
					Text(post.Title)
						.FontSize(18)
						.FontWeight(FontWeight.Bold)
						.Color(Colors.White)
						.LineBreakMode(LineBreakMode.WordWrap)
						.VerticalTextAlignment(TextAlignment.Center)
						.Cell(row: 4, column: 1),
				},
			}
			.Frame(height: 240)
			.ClipShape(new AsymmetricRoundedRectangle(12, 120, 12, 12));
		}
	}
}
