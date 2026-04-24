using System;
using System.Collections.Generic;
using System.Linq;
using Comet;
using Comet.Reactive;
using CometRecipeApp.Model;
using CometRecipeApp.Styles;
using Microsoft.Maui;
using Microsoft.Maui.Graphics;
using static Comet.CometControls;

namespace CometRecipeApp.Pages;

public class MainPage : View
{
	readonly Signal<int> _selectedRecipeIndex = new(-1);

	[Body]
	View body()
	{
		if (_selectedRecipeIndex.Value >= 0)
		{
			var recipe = RecipesData.DessertMenu[_selectedRecipeIndex.Value];
			return new RecipeDetailPage(recipe, () => _selectedRecipeIndex.Value = -1);
		}

		var cards = RecipesData.DessertMenu.Select(RenderRecipeCard).ToArray();

		return new ScrollView
		{
			new VStack(spacing: 16)
			{
				cards
			}.Padding(new Thickness(10, 15, 10, 20))
		}
		.Background(Colors.White);
	}

	View RenderRecipeCard(Recipe recipe)
	{
		return new Grid(columns: new object[] { "*", 140 })
		{
			new VStack(spacing: 6)
			{
				Text(recipe.Title)
					.FontSize(32)
					.FontWeight(FontWeight.Bold)
					.Color(AppColors.Black),

				Text(recipe.Description)
					.FontSize(12)
					.Color(AppColors.Black.WithAlpha(0.7f))
			}
			.Padding(new Thickness(10, 10, 0, 10))
			.Cell(row: 0, column: 0),

			Image(recipe.ImageSource)
				.Aspect(Aspect.AspectFit)
				.Frame(width: 140, height: 140)
				.Cell(row: 0, column: 1)
		}
		.Background(new SolidPaint(recipe.BgColor))
		.ClipShape(new RoundedRectangle(20))
		.Shadow(AppColors.BlackLight.WithAlpha(0.5f), radius: 30f, y: 6f)
		.Frame(height: 250)
		.FillHorizontal()
		.Margin(new Thickness(0, 5))
		.OnTap(_ => _selectedRecipeIndex.Value = Array.IndexOf(RecipesData.DessertMenu, recipe));
	}
}
