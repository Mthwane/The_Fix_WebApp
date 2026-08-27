namespace The__Fix_WebApp.Services
{
   
        
    public class PaymentInitResult
    {
        public bool Success { get; set; }
        public string? AuthorizationUrl { get; set; }
        public string? Reference { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class PaymentVerifyResult
    {
        public bool Success { get; set; }
        public string? Reference { get; set; }
        public decimal AmountRands { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public interface IPaymentService
    {
        Task<PaymentInitResult> InitializeTransactionAsync(string email, decimal amountRands, string reference, string callbackUrl);
        Task<PaymentVerifyResult> VerifyTransactionAsync(string reference);
    }
    
    }

