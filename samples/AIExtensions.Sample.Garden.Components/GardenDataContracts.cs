namespace AIExtensions.Sample.Garden.Components;

public static class GardenDataContracts
{
    public const string Product = "Product";
    public const string ProductList = "ProductList";
    public const string EmptyProductList = "EmptyProductList";
    public const string Cart = "Cart";
    public const string EmptyCart = "EmptyCart";
    public const string OrderList = "OrderList";
    public const string EmptyOrderList = "EmptyOrderList";
    public const string ReviewList = "ReviewList";
    public const string Recommendation = "Recommendation";
}

public interface IGardenComponentActions
{
    Task NavigateAsync(string destination);

    Task OpenProductAsync(string sku);

    Task AddToCartAsync(string sku);

    Task SetCartQuantityAsync(string sku, int quantity);

    Task RemoveFromCartAsync(string sku);

    Task OpenOrderAsync(string orderId);

    Task ReorderAsync(string orderId);
}
