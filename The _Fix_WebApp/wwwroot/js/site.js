// FashionFix client-side helpers.
// TODO: implement POS barcode-scan handling here:
//   1. Listen for input on #barcodeInput (keydown Enter, since most scanners act as keyboards).
//   2. fetch(`/POS/Product/${sku}`) to look up the product.
//   3. Append/increment a row in #cartTable and keep an in-memory cart array.
//   4. On #checkoutForm submit, serialize the cart array into hidden inputs
//      (CartItems[i].ProductId, CartItems[i].Quantity, CartItems[i].UnitPrice, etc.)
//      that model-bind to POSCheckoutViewModel.CartItems.
