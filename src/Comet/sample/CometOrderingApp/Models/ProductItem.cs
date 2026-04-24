namespace CometOrderingApp.Models;

enum ProductType
{
	Pizza,
	Salad,
	Dessert,
	Sides,
	Drinks
}

record ProductItem(int Id, ProductType Type, string Title, string Description, float Cost, string Image)
{
	public static ObservableCollection<ProductItem> Items { get; } = new ObservableCollection<ProductItem>(new[]
	{
		new ProductItem(Id: 1, Type: ProductType.Pizza, Title: "Margarita", Description: "Medium | Cheese , onion, and tomato pure", Cost: 12, Image: "margarita"),
		new ProductItem(Id: 2, Type: ProductType.Pizza, Title: "Classic Pepperoni", Description: "Medium | Chicken and onion", Cost: 13, Image: "pepperoni"),
		new ProductItem(Id: 3, Type: ProductType.Pizza, Title: "Chicken Supreme", Description: "Medium | Cheese, hungarian pepper, paneer, capsicum and onion", Cost: 10, Image: "chicken"),
		new ProductItem(Id: 4, Type: ProductType.Pizza, Title: "Vegetarian", Description: "Medium | Cheese , onion, and tomato pure", Cost: 21, Image: "vegetarian"),

		new ProductItem(Id: 5, Type: ProductType.Salad, Title: "Salad", Description: "Medium | Cheese , onion, and tomato pure", Cost: 5, Image: "salad"),
		new ProductItem(Id: 6, Type: ProductType.Salad, Title: "Salad", Description: "Medium | Chicken and onion", Cost: 5, Image: "salad"),
		new ProductItem(Id: 7, Type: ProductType.Salad, Title: "Salad", Description: "Medium | Cheese, hungarian pepper, paneer, capsicum and onion", Cost: 8, Image: "salad"),
		new ProductItem(Id: 8, Type: ProductType.Salad, Title: "Salad", Description: "Medium | Cheese , onion, and tomato pure", Cost: 10, Image: "salad"),

		new ProductItem(Id: 9, Type: ProductType.Dessert, Title: "Cheese Cake", Description: "Cheese Cake, onion, and tomato pure", Cost: 5, Image: "dessert"),
		new ProductItem(Id: 10, Type: ProductType.Dessert, Title: "Tiramisu", Description: "Tiramisu, onion, and tomato pure", Cost: 5, Image: "dessert"),

		new ProductItem(Id: 11, Type: ProductType.Sides, Title: "Chips", Description: "Chips pure", Cost: 5, Image: "sides"),
		new ProductItem(Id: 12, Type: ProductType.Sides, Title: "French fries", Description: "French fries, onion, and tomato pure", Cost: 5, Image: "sides"),

		new ProductItem(Id: 13, Type: ProductType.Drinks, Title: "Coke", Description: "Coke Cake, onion, and tomato pure", Cost: 8, Image: "drinks"),
		new ProductItem(Id: 14, Type: ProductType.Drinks, Title: "Water", Description: "Water, onion, and tomato pure", Cost: 7, Image: "drinks"),
		new ProductItem(Id: 15, Type: ProductType.Drinks, Title: "Fanta", Description: "Fanta pure", Cost: 2, Image: "drinks"),
		new ProductItem(Id: 16, Type: ProductType.Drinks, Title: "Orange Juice", Description: "Orange Juice fries, onion, and tomato pure", Cost: 5, Image: "drinks"),
		new ProductItem(Id: 17, Type: ProductType.Drinks, Title: "Lemon Tea", Description: "Lemon Tea, onion, and tomato pure", Cost: 3, Image: "drinks"),
	});
}
