namespace The__Fix_WebApp.Services
{
    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string? AuthorizationUrl { get; set; }
        public string? Reference { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// The reusable-charge token Paystack hands back for a completed transaction, plus
    /// display-only card details. AuthorizationCode is the only field ever persisted to our
    /// database (see CustomerPaymentMethod) - everything else here is for showing a
    /// "Visa ending in 4242" label, never for re-deriving the actual card number.
    /// </summary>
    public class PaystackAuthorization
    {
        public string AuthorizationCode { get; set; } = string.Empty;
        public string? Last4 { get; set; }
        public string? CardType { get; set; }
        public int? ExpiryMonth { get; set; }
        public int? ExpiryYear { get; set; }
        public string? Bank { get; set; }

        /// <summary>Paystack's own flag for whether this authorization can be charged again later - not every card/bank allows it.</summary>
        public bool Reusable { get; set; }
    }

    public class PaymentVerifyResult
    {
        public bool Success { get; set; }
        public string? Reference { get; set; }
        public decimal AmountRands { get; set; }
        public string? ErrorMessage { get; set; }

        /// <summary>Populated when Paystack returned card details for this transaction (null if unavailable).</summary>
        public PaystackAuthorization? Authorization { get; set; }
    }

    public interface IPaymentService
    {
        Task<PaymentInitResult> InitializeTransactionAsync(string email, decimal amountRands, string reference, string callbackUrl);
        Task<PaymentVerifyResult> VerifyTransactionAsync(string reference);

        /// <summary>Charges a previously-saved card directly - no redirect to Paystack's page, no re-entering card details.</summary>
        Task<PaymentVerifyResult> ChargeAuthorizationAsync(string email, decimal amountRands, string authorizationCode, string reference);
    }
}