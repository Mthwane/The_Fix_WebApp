// FashionFix POS client-side cart.
// Scanner input acts like a keyboard, so we listen for Enter on #barcodeInput, look the SKU
// up via GET /Pos/Product?sku=..., and show it in a preview modal (image, name, details,
// price, stock) before it's added - like a real till confirming what it just scanned. The
// cart itself lives in memory until checkout, where it's serialized into hidden inputs that
// model-bind to POSCheckoutViewModel.CartItems. Removing a line from the cart only removes
// it from THIS in-progress sale - it never touches the product record itself.
(function () {
    var barcodeInput = document.getElementById('barcodeInput');
    if (!barcodeInput) return; // Not on the POS page.

    var cart = []; // { productId, variantId, name, sku, size, color, quantity, unitPrice }
    var cartBody = document.getElementById('cartBody');
    var emptyCartRow = document.getElementById('emptyCartRow');
    var cartInputs = document.getElementById('cartInputs');
    var subtotalDisplay = document.getElementById('subtotalDisplay');
    var grandTotalDisplay = document.getElementById('grandTotalDisplay');
    // Discounts: the old free-typed amount is gone. The till only ever holds a validated CODE; the server works out
    // the amount (this value is just a preview) and re-validates everything again at checkout.
    var discountCodeInput = document.getElementById('discountCodeInput');
    var discountCodeHidden = document.getElementById('discountCodeHidden');
    var discountApplyBtn = document.getElementById('discountApplyBtn');
    var discountMessage = document.getElementById('discountMessage');
    var discountRow = document.getElementById('discountRow');
    var discountRowLabel = document.getElementById('discountRowLabel');
    var discountAmountDisplay = document.getElementById('discountAmountDisplay');
    var appliedDiscount = null;   // { code, amount, label }
    var appliedSig = '';
    var vatDisplay = document.getElementById('vatDisplay');
    var checkoutBtn = document.getElementById('checkoutBtn');
    var VAT_RATE = 0.15; // preview only - the server recalculates this authoritatively at checkout
    var scanError = document.getElementById('scanError');

    // Scan preview modal elements.
    var scanPreviewModalEl = document.getElementById('scanPreviewModal');
    var scanPreviewModal = scanPreviewModalEl ? new bootstrap.Modal(scanPreviewModalEl) : null;
    var scanPreviewImage = document.getElementById('scanPreviewImage');
    var scanPreviewName = document.getElementById('scanPreviewName');
    var scanPreviewDetails = document.getElementById('scanPreviewDetails');
    var scanPreviewPrice = document.getElementById('scanPreviewPrice');
    var scanPreviewStock = document.getElementById('scanPreviewStock');
    var scanPreviewQty = document.getElementById('scanPreviewQty');
    var scanPreviewAddBtn = document.getElementById('scanPreviewAddBtn');
    var pendingProduct = null;
    var FALLBACK_IMAGE = 'https://placehold.co/240x240?text=No+Image';

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
                '<td>' + line.name + (line.size || line.color ? ' <span class="pos-muted">(' + [line.size, line.color].filter(Boolean).join('/') + ')</span>' : '') + '</td>' +
                '<td>' + line.sku + '</td>' +
                '<td><input type="number" min="1" value="' + line.quantity + '" class="pos-input qty-input" style="width:70px;" data-index="' + index + '" /></td>' +
                '<td>' + formatCurrency(line.unitPrice) + '</td>' +
                '<td>' + formatCurrency(lineTotal) + '</td>' +
                '<td><button type="button" class="pos-btn-remove remove-btn" data-index="' + index + '" title="Remove from this till - does not delete the product">Remove</button></td>';
            cartBody.appendChild(row);

            var prefix = 'CartItems[' + index + ']';
            [
                ['ProductId', line.productId],
                ['VariantId', line.variantId],
                ['ProductName', line.name],
                ['SKU', line.sku],
                ['Size', line.size || ''],
                ['Color', line.color || ''],
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

        var sig = cart.map(function (l) { return l.variantId + 'x' + l.quantity; }).join(',');
        if (appliedDiscount && cart.length === 0) {
            appliedDiscount = null; appliedSig = '';
            setDiscountMessage('', false);
        } else if (appliedDiscount && sig !== appliedSig) {
            appliedSig = sig;
            applyDiscount(true); // basket changed - re-check the code against the new basket
        }

        var discount = appliedDiscount ? Math.min(appliedDiscount.amount, subtotal) : 0;
        if (discountCodeHidden) discountCodeHidden.value = appliedDiscount ? appliedDiscount.code : '';
        if (discountRow) {
            discountRow.style.display = appliedDiscount ? 'flex' : 'none';
            if (appliedDiscount) {
                discountRowLabel.textContent = 'Discount (' + appliedDiscount.code + ')';
                discountAmountDisplay.textContent = '-' + formatCurrency(discount);
            }
        }
        var taxableAmount = Math.max(0, subtotal - discount);
        var vat = Math.round(taxableAmount * VAT_RATE * 100) / 100;
        var grandTotal = taxableAmount + vat;

        subtotalDisplay.textContent = formatCurrency(subtotal);
        vatDisplay.textContent = formatCurrency(vat);
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

        // "Remove" only pulls the line out of THIS in-progress sale - it's cart state kept
        // in the browser, nothing is deleted from the product catalogue or the database.
        cartBody.querySelectorAll('.remove-btn').forEach(function (btn) {
            btn.addEventListener('click', function () {
                var idx = parseInt(this.getAttribute('data-index'), 10);
                cart.splice(idx, 1);
                render();
            });
        });
    }

    function addToCart(product, quantity) {
        var existing = cart.find(function (l) { return l.variantId === product.variantId; });
        if (existing) {
            existing.quantity += quantity;
        } else {
            cart.push({
                productId: product.productId,
                variantId: product.variantId,
                name: product.name,
                sku: product.sku,
                size: product.size,
                color: product.color,
                quantity: quantity,
                unitPrice: product.sellingPrice
            });
        }
        render();
    }

    function describeProduct(product) {
        if (product.description) return product.description;
        return [product.brand, product.category, product.size, product.color]
            .filter(function (part) { return part; })
            .join(' \u00b7 ');
    }

    function showScanPreview(product) {
        pendingProduct = product;

        scanPreviewImage.src = product.imageUrl || FALLBACK_IMAGE;
        scanPreviewImage.alt = product.name;
        scanPreviewName.textContent = product.name;
        scanPreviewDetails.textContent = describeProduct(product) || product.sku;
        scanPreviewPrice.textContent = formatCurrency(product.sellingPrice);
        scanPreviewStock.textContent = product.stockQuantity + ' in stock';
        scanPreviewQty.value = 1;
        scanPreviewQty.max = product.stockQuantity;

        if (scanPreviewModal) {
            scanPreviewModal.show();
        } else {
            // Bootstrap JS didn't load for some reason - fall back to adding directly
            // rather than silently doing nothing.
            addToCart(product, 1);
        }
    }

    if (scanPreviewAddBtn) {
        scanPreviewAddBtn.addEventListener('click', function () {
            if (!pendingProduct) return;
            var qty = parseInt(scanPreviewQty.value, 10);
            if (!qty || qty < 1) qty = 1;
            addToCart(pendingProduct, qty);
            pendingProduct = null;
            scanPreviewModal.hide();
            barcodeInput.value = '';
            barcodeInput.focus();
        });
    }

    // Re-focus the scanner input whenever the modal closes (including Cancel/Esc/backdrop
    // click), so the till is always ready for the next scan without the cashier re-clicking.
    if (scanPreviewModalEl) {
        scanPreviewModalEl.addEventListener('hidden.bs.modal', function () {
            pendingProduct = null;
            barcodeInput.value = '';
            barcodeInput.focus();
        });
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
                showScanPreview(product);
            })
            .catch(function () {
                scanError.textContent = 'No product found for SKU "' + sku + '".';
            });
    });

    function setDiscountMessage(text, ok) {
        if (!discountMessage) return;
        discountMessage.textContent = text || '';
        discountMessage.style.color = ok ? 'var(--stf-primary)' : 'var(--stf-error)';
    }

    function clearDiscount(message) {
        appliedDiscount = null;
        appliedSig = '';
        setDiscountMessage(message || '', false);
        render();
    }

    function applyDiscount(silent) {
        if (!discountCodeInput) return;
        var code = (appliedDiscount && silent ? appliedDiscount.code : discountCodeInput.value).trim();
        if (!code) { if (!silent) setDiscountMessage('Enter a discount code first.', false); return; }
        if (cart.length === 0) { setDiscountMessage('Scan at least one item first.', false); return; }

        var form = document.getElementById('checkoutForm');
        var token = form.querySelector('input[name="__RequestVerificationToken"]');
        var body = new FormData();
        if (token) body.append('__RequestVerificationToken', token.value);
        body.append('code', code);
        var cust = form.querySelector('input[name="CustomerId"]');
        if (cust && cust.value.trim()) body.append('customerId', cust.value.trim());
        cart.forEach(function (l) { body.append('variantIds', l.variantId); body.append('quantities', l.quantity); });

        if (discountApplyBtn) discountApplyBtn.disabled = true;
        fetch('/Pos/ApplyDiscount', { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (res) {
                if (res.valid) {
                    appliedDiscount = { code: res.code, amount: res.amount, label: res.label };
                    appliedSig = cart.map(function (l) { return l.variantId + 'x' + l.quantity; }).join(',');
                    discountCodeInput.value = res.code;
                    setDiscountMessage(res.name + ' - ' + res.label + ' applied.', true);
                    render();
                } else {
                    clearDiscount(res.message || 'That code could not be applied.');
                }
            })
            .catch(function () { setDiscountMessage('Could not check the code - please try again.', false); })
            .then(function () { if (discountApplyBtn) discountApplyBtn.disabled = false; });
    }

    if (discountApplyBtn) {
        discountApplyBtn.addEventListener('click', function () { applyDiscount(false); });
        discountCodeInput.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); applyDiscount(false); }
        });
        discountCodeInput.addEventListener('input', function () {
            // Editing the code un-applies the previous one so a stale discount can't linger.
            if (appliedDiscount && discountCodeInput.value.trim().toUpperCase() !== appliedDiscount.code) clearDiscount('');
        });
        document.querySelectorAll('.discount-pick').forEach(function (btn) {
            btn.addEventListener('click', function () {
                discountCodeInput.value = btn.getAttribute('data-code');
                applyDiscount(false);
            });
        });
        var custField = document.querySelector('#checkoutForm input[name="CustomerId"]');
        if (custField) custField.addEventListener('change', function () { if (appliedDiscount) applyDiscount(true); });
    }

    document.getElementById('checkoutForm').addEventListener('submit', function (e) {
        if (cart.length === 0) {
            e.preventDefault();
            scanError.textContent = 'Add at least one item before checking out.';
        }
    });

    // If the last checkout attempt was rejected server-side (stock check, validation, etc.),
    // the model is posted back with the same cart - restore it instead of losing the sale.
    var restoreDataEl = document.getElementById('restoreCartData');
    if (restoreDataEl && restoreDataEl.textContent.trim()) {
        try {
            var restored = JSON.parse(restoreDataEl.textContent);
            if (Array.isArray(restored) && restored.length > 0) {
                cart = restored;
            }
        } catch (e) { /* ignore malformed restore payload */ }
    }

    render();

    // A rejected checkout posts the typed code back - re-check it against the restored basket.
    if (discountCodeInput && discountCodeInput.value.trim() && cart.length > 0) applyDiscount(false);
})();
