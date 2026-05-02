using System;
using System.Linq;
using Microsoft.Maui.Graphics;
using CometRecipeApp.Styles;

namespace CometRecipeApp.Model;

public record Recipe(
	int Id,
	string Title,
	string Description,
	string[] Ingredients,
	IndexItem[] Instructions,
	string Image,
	string BgImageName,
	Color BgColor
)
{
	// MAUI resource name: lowercase, no extension, underscores
	public string ImageSource => $"i_{Image.Replace("-", "_").Replace(".png", "")}";

	public string? BgImage =>
		string.IsNullOrEmpty(BgImageName) ? null : $"i_{BgImageName.Replace("-", "_")}";
}

public record IndexItem(int Index, string Item);

public static class RecipesData
{
	public static Recipe[] DessertMenu = new[]
	{
		new Recipe(
			Id: 1,
			Title: "Lemon Cheesecake",
			Description: "Tart Lemon Cheesecake sits atop an almond-graham cracker crust to add a delightful nuttiness to the traditional graham cracker crust. Finish the cheesecake with lemon curd for double the tart pucker!",
			Ingredients: new[]
			{
				"110g digestive biscuits",
				"50g butter",
				"25g light brown soft sugar",
				"350g mascarpone",
				"75g caster sugar",
				"1 lemon, zested",
				"2-3 lemons, juiced (about 90ml)",
			},
			Instructions: new[]
			{
				new IndexItem(1, "Crush the digestive biscuits in a food bag with a rolling pin or in the food processor. Melt the butter in a saucepan, take off heat and stir in the brown sugar and biscuit crumbs."),
				new IndexItem(2, "Line the base of a 20cm loose bottomed cake tin with baking parchment. Press the biscuit into the bottom of the tin and chill in the fridge while making the topping."),
				new IndexItem(3, "Beat together the mascarpone, caster sugar, lemon zest and juice, until smooth and creamy. Spread over the base and chill for a couple of hours."),
			},
			Image: "01-lemon-cheesecake.png",
			BgImageName: "01-lemon-cheesecake-bg",
			BgColor: AppColors.Yellow
		),
		new Recipe(
			Id: 2,
			Title: "Macaroons",
			Description: "Soft and chewy on the inside, crisp and golden on the outside — these are the perfect macaroons.",
			Ingredients: new[]
			{
				"1 ¾ cups powdered sugar (210 g)",
				"1 cup almond flour (95 g), finely ground",
				"1 teaspoon salt, divided",
				"3 egg whites, at room temperature",
				"¼ cup granulated sugar (50 g)",
				"½ teaspoon vanilla extract",
				"2 drops pink gel food coloring",
			},
			Instructions: new[]
			{
				new IndexItem(1, "Combine the powdered sugar, almond flour, and ½ teaspoon of salt, and process on low speed, until extra fine. Sift through a fine-mesh sieve."),
				new IndexItem(2, "Beat the egg whites and remaining ½ teaspoon of salt until soft peaks form. Gradually add the granulated sugar until stiff peaks form."),
				new IndexItem(3, "Add the vanilla and food coloring and beat until just combined."),
				new IndexItem(4, "Fold in the sifted almond flour mixture until the batter falls into ribbons."),
				new IndexItem(5, "Pipe macarons onto parchment paper in 1½-inch circles. Tap the sheet 5 times to release air bubbles."),
				new IndexItem(6, "Let sit at room temperature for 30 minutes to 1 hour, until dry to the touch."),
				new IndexItem(7, "Bake at 300°F for 17 minutes, until the feet are well-risen."),
				new IndexItem(8, "Cool completely before filling with buttercream."),
			},
			Image: "05-macaroons.png",
			BgImageName: "",
			BgColor: AppColors.Primary
		),
		new Recipe(
			Id: 3,
			Title: "Cream Cupcakes",
			Description: "Bake these easy vanilla cupcakes in just 35 minutes. Perfect for birthdays, picnics or whenever you fancy a sweet treat, they're sure to be a crowd-pleaser.",
			Ingredients: Array.Empty<string>(),
			Instructions: Array.Empty<IndexItem>(),
			Image: "08-cream-cupcakes.png",
			BgImageName: "",
			BgColor: AppColors.PinkLight
		),
		new Recipe(
			Id: 4,
			Title: "Chocolate Cheesecake",
			Description: "Treat family and friends to this decadent chocolate dessert. It's an indulgent end to a dinner party or weekend family meal.",
			Ingredients: new[]
			{
				"150g digestive biscuits (about 10)",
				"1 tbsp caster sugar",
				"45g butter, melted",
				"150g dark chocolate",
				"120ml double cream",
				"2 tsp cocoa powder",
				"200g full-fat cream cheese",
				"115g caster sugar",
			},
			Instructions: new[]
			{
				new IndexItem(1, "Crush the digestive biscuits, then tip into a bowl with the sugar and butter and stir to combine. Press into a 18cm springform tin and chill for 30 mins."),
				new IndexItem(2, "Melt the chocolate, then leave to cool slightly. Whip the cream until soft peaks form, fold in the cocoa powder. Beat the cream cheese and sugar, fold in the cream mixture and cooled chocolate."),
				new IndexItem(3, "Spoon over the biscuit base. Freeze for 2 hrs, then soften at room temperature for 20 mins before serving."),
			},
			Image: "02-chocolate-cake-1.png",
			BgImageName: "",
			BgColor: AppColors.OrangeDark
		),
		new Recipe(
			Id: 5,
			Title: "Fruit Plate",
			Description: "Melons — they're firmer so make a great base for the softer berries and fruits. Tropical fruit — the top of a pineapple can be included for height, while dragonfruit looks vibrant.",
			Ingredients: Array.Empty<string>(),
			Instructions: Array.Empty<IndexItem>(),
			Image: "09-fruit-plate.png",
			BgImageName: "",
			BgColor: AppColors.Green
		),
		new Recipe(
			Id: 6,
			Title: "Chocolate Donuts",
			Description: "Moist and fluffy donuts that are baked, not fried, and full of chocolate. Covered in a thick chocolate glaze, these are perfect for any chocoholic.",
			Ingredients: new[]
			{
				"1 cup (140g) all-purpose flour",
				"1/4 cup (25g) unsweetened cocoa powder",
				"1/2 teaspoon baking powder",
				"1/2 teaspoon baking soda",
				"1 large egg",
				"1/2 cup (100g) granulated sugar",
				"1/3 cup (80 ml) milk",
				"1/4 cup (60 ml) yogurt",
				"2 tablespoons (30g) unsalted butter, melted",
			},
			Instructions: new[]
			{
				new IndexItem(1, "Preheat oven to 350°F/180°. Grease a donut pan."),
				new IndexItem(2, "Whisk together the flour, cocoa powder, baking powder, baking soda, and salt."),
				new IndexItem(3, "Whisk egg with sugar, add milk, yogurt, melted butter and vanilla. Pour into flour mixture."),
				new IndexItem(4, "Fill donut cavities ¾ full using a piping bag."),
				new IndexItem(5, "Bake for 9–10 minutes. Cool for 5 minutes in pan, then transfer to wire rack."),
				new IndexItem(6, "Make chocolate glaze: Melt chocolate, cream, and butter. Dip donut tops into glaze."),
			},
			Image: "03-chocolate-donuts.png",
			BgImageName: "",
			BgColor: AppColors.Sugar
		),
		new Recipe(
			Id: 7,
			Title: "Strawberry Cake",
			Description: "Jam-packed with fresh strawberries, this strawberry cake is one of the simplest, most delicious cakes you'll ever make.",
			Ingredients: Array.Empty<string>(),
			Instructions: Array.Empty<IndexItem>(),
			Image: "13-strawberry-powdered-cake.png",
			BgImageName: "",
			BgColor: AppColors.Red
		),
		new Recipe(
			Id: 8,
			Title: "Fluffy Cake",
			Description: "This is a very good everyday cake leavened with baking powder. It's relatively light — it isn't loaded with butter, and it calls for only 2 eggs and 2 percent milk.",
			Ingredients: new[]
			{
				"1/2 cup unsalted butter, softened",
				"2 1/4 cups all-purpose flour",
				"1 1/3 cups granulated sugar",
				"1 tablespoon baking powder",
				"1/2 teaspoon salt",
				"1 tablespoon vanilla extract",
				"1 cup 2% milk, room temperature",
				"2 large eggs, room temperature",
			},
			Instructions: new[]
			{
				new IndexItem(1, "Preheat oven to 350°F. Butter and flour two 9-inch cake pans."),
				new IndexItem(2, "Combine the sugar, flour, baking powder, and salt. Add butter and blend until sandy."),
				new IndexItem(3, "Add vanilla extract and milk. Stop and scrape, then mix for another minute."),
				new IndexItem(4, "Add eggs one at a time, mixing well after each. Beat until fluffy."),
				new IndexItem(5, "Pour batter into prepared pans. Bake for about 30 minutes."),
				new IndexItem(6, "Cool in pans for 10 minutes, then turn out onto wire racks. Cool 1 hour before frosting."),
			},
			Image: "04-fluffy-cake.png",
			BgImageName: "",
			BgColor: AppColors.OrangeDark
		),
	};
}
