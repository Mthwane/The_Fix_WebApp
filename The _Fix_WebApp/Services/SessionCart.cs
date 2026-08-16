using System.Text.Json;
using FashionFix.Web.Models.ViewModels;
using Microsoft.AspNetCore.Http;

namespace FashionFix.Web.Services;

/// <summary>
/// Reads/writes the customer's shopping cart to their session. Deliberately NOT in the
/// database - an in-progress cart is throwaway state, so this avoids a migration and keeps
/// abandoned carts from ever cluttering the Orders table.
/// </summary>
public static class SessionCart
{
    private const string SessionKey = "Cart";

    public static CartViewModel Get(ISession session)
    {
        var json = session.GetString(SessionKey);
        if (string.IsNullOrEmpty(json)) return new CartViewModel();

        var lines = JsonSerializer.Deserialize<List<CartLineViewModel>>(json) ?? new();
        return new CartViewModel { Lines = lines };
    }

    public static void Save(ISession session, CartViewModel cart)
    {
        session.SetString(SessionKey, JsonSerializer.Serialize(cart.Lines));
    }

    public static void Clear(ISession session)
    {
        session.Remove(SessionKey);
    }
}
