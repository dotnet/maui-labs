using System;
using System.Collections.Generic;
using System.Linq;
using Comet;
using CometRecipeApp.Model;
using CometRecipeApp.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometRecipeApp.Pages;

public class RecipeDetailPage : View
{
	readonly Recipe _recipe;
	readonly Action? _onDismiss;

	public RecipeDetailPage(Recipe recipe, Action? onDismiss = null)
	{
		_recipe = recipe;
		_onDismiss = onDismiss;
	}

	[Body]
	View body()
	{
		var contentItems = new List<View>
		{
			// Header with image
			RenderHeader(),

			// Title
			Text(_recipe.Title)
				.FontSize(32)
				.FontWeight(FontWeight.Bold)
				.Color(AppColors.Black)
				.Padding(new Thickness(15, 20, 15, 0)),

			// Description
			Text(_recipe.Description)
				.FontSize(16)
				.Color(Colors.Black)
				.Padding(new Thickness(15, 8, 15, 0)),
		};

		// Ingredients
		if (_recipe.Ingredients.Length > 0 && !string.IsNullOrEmpty(_recipe.Ingredients[0]))
		{
			contentItems.Add(
				Text("INGREDIENTS")
					.FontSize(14)
					.FontWeight(FontWeight.Heavy)
					.Color(AppColors.Black)
					.Padding(new Thickness(15, 20, 15, 4))
			);

			foreach (var ingredient in _recipe.Ingredients.Where(i => !string.IsNullOrEmpty(i)))
			{
				contentItems.Add(RenderIngredientItem(ingredient));
			}
		}

		// Instructions
		if (_recipe.Instructions.Length > 0)
		{
			contentItems.Add(
				Text("STEPS")
					.FontSize(14)
					.FontWeight(FontWeight.Heavy)
					.Color(AppColors.Black)
					.Padding(new Thickness(15, 20, 15, 4))
			);

			foreach (var step in _recipe.Instructions)
			{
				contentItems.Add(RenderStepItem(step));
			}
		}

		// Bottom spacer
		contentItems.Add(new Spacer().Frame(height: 40));

		return new Grid
		{
			// Scrollable content
			new ScrollView
			{
				new VStack(spacing: 0)
				{
					contentItems.ToArray()
				}
			},

			// Back button overlay at top-left
			new VStack
			{
				new HStack
				{
					RenderBackButton(),
					new Spacer()
				},
				new Spacer()
			}
		}
		.Background(Colors.White);
	}

	View RenderBackButton()
	{
		return new ZStack
		{
			new ShapeView(new RoundedRectangle(21))
				.Background(new SolidPaint(Colors.Black))
				.Frame(width: 42, height: 42),

			Text("←")
				.FontSize(22)
				.Color(Colors.White)
		}
		.Frame(width: 42, height: 42)
		.Margin(new Thickness(10))
		.OnTap(_ => _onDismiss?.Invoke());
	}

	View RenderHeader()
	{
		var children = new List<View>();

		if (_recipe.BgImage != null)
		{
			children.Add(
				Image(_recipe.BgImage)
					.Aspect(Aspect.AspectFill)
					.FillHorizontal()
					.FillVertical()
			);
		}

		children.Add(
			Image(_recipe.ImageSource)
				.Aspect(Aspect.AspectFit)
				.Frame(width: 200, height: 200)
				.Margin(new Thickness(20))
		);

		return new ZStack
		{
			children.ToArray()
		}
		.Background(new SolidPaint(_recipe.BgColor))
		.ClipShape(new AsymmetricRoundedRectangle(0, 0, 20, 20))
		.Shadow(AppColors.BlackLight.WithAlpha(0.5f), radius: 30f, y: 15f)
		.Frame(height: 300)
		.FillHorizontal();
	}

	View RenderIngredientItem(string ingredient)
	{
		return new HStack(spacing: 12)
		{
			new ZStack
			{
				new ShapeView(new RoundedRectangle(5))
					.Background(new SolidPaint(_recipe.BgColor))
					.Frame(width: 42, height: 42),

				Image("chef")
					.Aspect(Aspect.AspectFit)
					.Frame(width: 24, height: 24)
			}
			.Frame(width: 42, height: 42),

			Text(ingredient)
				.FontSize(15)
				.Color(AppColors.Black)
		}
		.Padding(new Thickness(15, 5));
	}

	View RenderStepItem(IndexItem step)
	{
		return new HStack(spacing: 12)
		{
			new ZStack
			{
				new ShapeView(new RoundedRectangle(5))
					.Background(new SolidPaint(_recipe.BgColor))
					.Frame(width: 32, height: 32),

				Text(step.Index.ToString())
					.FontSize(22)
					.FontWeight(FontWeight.Heavy)
					.Color(AppColors.Black)
					.Rotation(45)
			}
			.Frame(width: 32, height: 32),

			Text(step.Item)
				.FontSize(15)
				.Color(AppColors.Black)
				.FillHorizontal()
		}
		.Padding(new Thickness(15, 5));
	}
}
