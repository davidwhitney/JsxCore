namespace SampleApp.Services;

/// <summary>A stock item, as seen from a view.</summary>
/// <param name="Sku">Stock keeping unit.</param>
/// <param name="Name">Display name.</param>
/// <param name="Quantity">Units on hand.</param>
/// <param name="UnitPrice">Price per unit.</param>
public sealed record StockItem(string Sku, string Name, int Quantity, decimal UnitPrice);

/// <summary>
/// A plain service, registered as a JsxCore global so server-rendered views can call it directly.
/// Nothing here knows about JavaScript.
/// </summary>
public sealed class InventoryService
{
    private static readonly StockItem[] Items =
    [
        new("SKU-1001", "Mechanical keyboard", 12, 89.00m),
        new("SKU-1002", "27\" monitor", 4, 249.50m),
        new("SKU-1003", "USB-C dock", 38, 129.99m),
        new("SKU-1004", "Desk mat", 7, 24.00m),
        new("SKU-1005", "Webcam", 61, 59.95m)
    ];

    /// <summary>Total value of everything on hand.</summary>
    public decimal GetTotalValue() => Items.Sum(item => item.Quantity * item.UnitPrice);

    /// <summary>Items at or below a stock threshold.</summary>
    public StockItem[] GetLowStock(int threshold) =>
        Items.Where(item => item.Quantity <= threshold).ToArray();

    /// <summary>Everything in stock.</summary>
    public StockItem[] GetAll() => Items;
}

/// <summary>Exposes server time to views, to show a second global alongside the first.</summary>
public sealed class ClockService(TimeProvider timeProvider)
{
    /// <summary>The current server time, formatted.</summary>
    public string Now() => timeProvider.GetLocalNow().ToString("HH:mm:ss");

    /// <summary>The server's time zone id.</summary>
    public string TimeZone() => timeProvider.LocalTimeZone.Id;
}
