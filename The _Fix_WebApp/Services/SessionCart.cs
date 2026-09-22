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

    // --- Checkout snapshot ---
    // A separate, reference-keyed copy of the cart taken the moment a Paystack transaction is
    // initialized - deliberately independent of the live "Cart" key above. Without this, the
    // live cart could be edited (in another tab) between InitializeTransactionAsync and the
    // Paystack callback, letting a customer pay for a cheap item and receive whatever the cart
    // happens to contain when the callback runs instead. PaymentsController.Callback builds the
    // order from THIS snapshot, never from SessionCart.Get.
    private static string SnapshotKey(string reference) => $"CheckoutSnapshot:{reference}";

    public static void SaveSnapshot(ISession session, string reference, CartViewModel cart)
    {
        session.SetString(SnapshotKey(reference), JsonSerializer.Serialize(cart.Lines));
    }

    public static CartViewModel? GetSnapshot(ISession session, string reference)
    {
        var json = session.GetString(SnapshotKey(reference));
        if (string.IsNullOrEmpty(json)) return null;

        var lines = JsonSerializer.Deserialize<List<CartLineViewModel>>(json) ?? new();
        return new CartViewModel { Lines = lines };
    }

    public static void ClearSnapshot(ISession session, string reference)
    {
        session.Remove(SnapshotKey(reference));
    }
}
