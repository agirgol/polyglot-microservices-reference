namespace Orders.Domain;

/// <summary>
/// Where an order is in its life. Cancelled is terminal; nothing leaves it.
/// </summary>
public enum OrderStatus
{
    Placed,
    Confirmed,
    Cancelled,
}
