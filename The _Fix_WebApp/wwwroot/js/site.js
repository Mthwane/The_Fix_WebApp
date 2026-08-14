// FashionFix POS client-side cart.
// Scanner input acts like a keyboard, so we listen for Enter on #barcodeInput,
// look the SKU up via GET /Pos/Product/{sku}, and keep the cart in memory until
// checkout - at which point it's serialized into hidden inputs that model-bind
// to POSCheckoutViewModel.CartItems.
(function () {
    var barcodeInput = document.getElementById('barcodeInput');
    if (!barcodeInput) return; // Not on the POS page.

    var cart = []; // { productId, name, sku, quantity, unitPrice }
    var cartBody = document.getElementById('cartBody');
    var emptyCartRow = document.getElementById('emptyCartRow');
    var cartInputs = document.getElementById('cartInputs');
    var subtotalDisplay = document.getElementById('subtotalDisplay');
    var grandTotalDisplay = document.getElementById('grandTotalDisplay');
    var discountInput = document.getElementById('discountInput');
    var taxInput = document.getElementById('taxInput');
    var checkoutBtn = document.getElementById('checkoutBtn');
    var scanError = document.getElementById('scanError');

    function formatCurrency(value) {
        return 'R' + value.toFixed(2);
    }

    function render() {
        cartBody.innerHTML = '';
        cartInputs.innerHTML = '';

        if (cart.length === 0) {
            cartBody.appendChild(emptyCartRow);
        }

        var subtotal = 0;

        cart.forEach(function (line, index) {
            var lineTotal = line.quantity * line.unitPrice;
            subtotal += lineTotal;

            var row = document.createElement('tr');
            row.innerHTML =
                '<td>' + line.name + '</td>' +
                '<td>' + line.sku + '</td>' +
                '<td><input type="number" min="1" value="' + line.quantity + '" class="form-control form-control-sm qty-input" style="width:70px;" data-index="' + index + '" /></td>' +
                '<td>' + formatCurrency(line.unitPrice) + '</td>' +
                '<td>' + formatCurrency(lineTotal) + '</td>' +
                '<td><button type="button" class="btn btn-sm btn-outline-danger remove-btn" data-index="' + index + '">&times;</button></td>';
            cartBody.appendChild(row);

            var prefix = 'CartItems[' + index + ']';
            [
                ['ProductId', line.productId],
                ['ProductName', line.name],
                ['SKU', line.sku],
                ['Quantity', line.quantity],
                ['UnitPrice', line.unitPrice]
            ].forEach(function (pair) {
                var hidden = document.createElement('input');
                hidden.type = 'hidden';
                hidden.name = prefix + '.' + pair[0];
                hidden.value = pair[1];
                cartInputs.appendChild(hidden);
            });
        });

        var discount = parseFloat(discountInput.value) || 0;
        var tax = parseFloat(taxInput.value) || 0;
        var grandTotal = subtotal - discount + tax;

        subtotalDisplay.textContent = formatCurrency(subtotal);
        grandTotalDisplay.textContent = formatCurrency(grandTotal < 0 ? 0 : grandTotal);
        checkoutBtn.disabled = cart.length === 0;

        cartBody.querySelectorAll('.qty-input').forEach(function (input) {
            input.addEventListener('change', function () {
                var idx = parseInt(this.getAttribute('data-index'), 10);
                var qty = parseInt(this.value, 10);
                cart[idx].quantity = qty > 0 ? qty : 1;
                render();
            });
        });

        cartBody.querySelectorAll('.remove-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(this.getAttribute('data-index'), 10);
                cart.splice(idx, 1);
                render();
            });
        });
    }

    function addToCart(product) {
        var existing = cart.find(function (l) { return l.productId === product.productId; });
        if (existing) {
            existing.quantity += 1;
        } else {
            cart.push({
                productId: product.productId,
                name: product.name,
                sku: product.sku,
                quantity: 1,
                unitPrice: product.sellingPrice
            });
        }
        render();
    }

    barcodeInput.addEventListener('keydown', function (e) {
        if (e.key !== 'Enter') return;
        e.preventDefault();

        var sku = barcodeInput.value.trim();
        if (!sku) return;

        scanError.textContent = '';

        fetch('/Pos/Product?sku=' + encodeURIComponent(sku))
            .then(function (res) {
                if (!res.ok) throw new Error('not found');
                return res.json();
            })
            .then(function (product) {
                if (product.stockQuantity <= 0) {
                    scanError.textContent = product.name + ' is out of stock.';
                    return;
                }
                addToCart(product);
                barcodeInput.value = '';
            })
            .catch(function () {
                scanError.textContent = 'No product found for SKU "' + sku + '".';
            });
    });

    discountInput.addEventListener('input', render);
    taxInput.addEventListener('input', render);

    document.getElementById('checkoutForm').addEventListener('submit', function (e) {
        if (cart.length === 0) {
            e.preventDefault();
            scanError.textContent = 'Add at least one item before checking out.';
        }
    });

    render();
})();
