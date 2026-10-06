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
    var autoSig = '';             // basket signature the last automatic-discount check was run for
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

    // Till persistence: the cart used to live only in memory, so opening the catalogue (or any other page) and coming back
    // wiped the sale. It's now mirrored into sessionStorage (this tab only, gone when the tab closes), keyed to the signed-in
    // user so a different cashier on the same tab never inherits it. Cleared when the sale completes (Receipt page).
    var TILL_KEY = 'ff.pos.till';
    var tillUser = (document.getElementById('cartTable') || {}).getAttribute
        ? document.getElementById('cartTable').getAttribute('data-user') || '' : '';
    function tillField(name) {
        var el = document.querySelector('#checkoutForm [name="' + name + '"]');
        return el ? el.value : '';
    }
    function saveTill() {
        try {
            if (cart.length === 0) { sessionStorage.removeItem(TILL_KEY); return; }
            sessionStorage.setItem(TILL_KEY, JSON.stringify({
                user: tillUser,
                cart: cart,
                discountCode: appliedDiscount && !appliedDiscount.auto ? appliedDiscount.code : (discountCodeInput ? discountCodeInput.value : ''),
                customerId: tillField('CustomerId'),
                receiptEmail: tillField('ReceiptEmail'),
                paymentMethod: tillField('PaymentMethod')
            }));
        } catch (e) { /* storage unavailable - the till still works, it just won't survive navigation */ }
    }
    function loadTill() {
        try {
            var saved = JSON.parse(sessionStorage.getItem(TILL_KEY) || 'null');
            if (!saved || saved.user !== tillUser || !Array.isArray(saved.cart) || saved.cart.length === 0) return null;
            return saved;
        } catch (e) { return null; }
    }
    ['CustomerId', 'ReceiptEmail', 'PaymentMethod'].forEach(function (n) {
        var el = document.querySelector('#checkoutForm [name="' + n + '"]');
        if (el) { el.addEventListener('input', saveTill); el.addEventListener('change', saveTill); }
    });

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
            appliedDiscount = null; appliedSig = ''; autoSig = '';
            setDiscountMessage('', false);
        } else if (appliedDiscount && !appliedDiscount.auto && sig !== appliedSig) {
            appliedSig = sig;
            applyDiscount(true); // basket changed - re-check the code against the new basket
        } else if (!appliedDiscount || appliedDiscount.auto) {
            // No typed code in play: let the server say whether an automatic discount covers this basket.
            if (cart.length === 0) autoSig = '';
            else if (sig !== autoSig) { autoSig = sig; checkAutoDiscount(); }
        }

        var discount = appliedDiscount ? Math.min(appliedDiscount.amount, subtotal) : 0;
        // An automatic discount is NOT posted as a code - the server re-finds it itself (any cashier, no permission needed).
        if (discountCodeHidden) discountCodeHidden.value = appliedDiscount && !appliedDiscount.auto ? appliedDiscount.code : '';
        if (discountRow) {
            discountRow.style.display = appliedDiscount ? 'flex' : 'none';
            if (appliedDiscount) {
                discountRowLabel.textContent = (appliedDiscount.auto ? 'Auto discount (' : 'Discount (') + appliedDiscount.code + ')';
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
        saveTill();

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

    // ---- Name search: typing shows matching items under the box; clicking one opens the same preview as a scan. ----
    var searchResults = document.getElementById('searchResults');
    var searchTimer = null;
    var searchSeq = 0;

    function hideSearchResults() {
        if (!searchResults) return;
        searchResults.style.display = 'none';
        searchResults.innerHTML = '';
    }

    function pickProduct(product) {
        hideSearchResults();
        scanError.textContent = '';
        if (product.stockQuantity <= 0) {
            scanError.textContent = product.name + ' is out of stock.';
            return;
        }
        showScanPreview(product);
    }

    function renderSearchResults(items) {
        if (!searchResults) return;
        searchResults.innerHTML = '';
        if (!items.length) {
            var none = document.createElement('div');
            none.style.cssText = 'padding:10px 14px;font-size:0.85rem;color:var(--stf-on-surface-variant);';
            none.textContent = 'No items match that search.';
            searchResults.appendChild(none);
            searchResults.style.display = 'block';
            return;
        }
        items.forEach(function (item) {
            var row = document.createElement('div');
            row.style.cssText = 'display:flex;justify-content:space-between;align-items:center;gap:10px;padding:10px 14px;cursor:pointer;border-bottom:1px solid var(--stf-surface-container);';
            var left = document.createElement('div');
            var title = document.createElement('div');
            title.style.cssText = 'font-weight:600;font-size:0.9rem;';
            title.textContent = item.name;
            var sub = document.createElement('div');
            sub.style.cssText = 'font-size:0.78rem;color:var(--stf-on-surface-variant);';
            sub.textContent = [item.size, item.color, item.sku].filter(function (x) { return x; }).join(' \u00b7 ');
            left.appendChild(title);
            left.appendChild(sub);
            var right = document.createElement('div');
            right.style.cssText = 'text-align:right;font-size:0.82rem;white-space:nowrap;';
            right.textContent = formatCurrency(item.sellingPrice) + ' \u00b7 ' + (item.stockQuantity > 0 ? item.stockQuantity + ' in stock' : 'Out of stock');
            if (item.stockQuantity <= 0) right.style.color = 'var(--stf-error)';
            row.appendChild(left);
            row.appendChild(right);
            row.addEventListener('mousedown', function (ev) { ev.preventDefault(); pickProduct(item); });
            searchResults.appendChild(row);
        });
        searchResults.style.display = 'block';
    }

    barcodeInput.addEventListener('input', function () {
        var q = barcodeInput.value.trim();
        clearTimeout(searchTimer);
        if (q.length < 2) { hideSearchResults(); return; }
        searchTimer = setTimeout(function () {
            var seq = ++searchSeq;
            fetch('/Pos/Search?q=' + encodeURIComponent(q))
                .then(function (res) { return res.ok ? res.json() : []; })
                .then(function (items) { if (seq === searchSeq) renderSearchResults(items); })
                .catch(function () { /* search is a convenience; scanning still works */ });
        }, 250);
    });

    barcodeInput.addEventListener('blur', function () { setTimeout(hideSearchResults, 150); });

    barcodeInput.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') { hideSearchResults(); return; }
        if (e.key !== 'Enter') return;
        e.preventDefault();

        var sku = barcodeInput.value.trim();
        if (!sku) return;

        clearTimeout(searchTimer);
        searchSeq++; // drop any search still in flight; Enter decides now
        hideSearchResults();
        scanError.textContent = '';

        // Exact SKU first (what a barcode scanner sends). If that misses, treat the text as a name search:
        // one match opens the preview straight away, several show the list to pick from.
        fetch('/Pos/Product?sku=' + encodeURIComponent(sku))
            .then(function (res) {
                if (!res.ok) throw new Error('not found');
                return res.json();
            })
            .then(function (product) { pickProduct(product); })
            .catch(function () {
                fetch('/Pos/Search?q=' + encodeURIComponent(sku))
                    .then(function (res) { return res.ok ? res.json() : []; })
                    .then(function (items) {
                        if (items.length === 1) { pickProduct(items[0]); }
                        else if (items.length > 1) { renderSearchResults(items); }
                        else { scanError.textContent = 'No product found for "' + sku + '".'; }
                    })
                    .catch(function () { scanError.textContent = 'No product found for "' + sku + '".'; });
            });
    });

    function setDiscountMessage(text, ok) {
        if (!discountMessage) return;
        discountMessage.textContent = text || '';
        discountMessage.style.color = ok ? 'var(--stf-primary)' : 'var(--stf-error)';
    }

    // Asks the server for the best automatic discount for the current basket (no code involved).
    function checkAutoDiscount() {
        var form = document.getElementById('checkoutForm');
        var token = form.querySelector('input[name="__RequestVerificationToken"]');
        var body = new FormData();
        if (token) body.append('__RequestVerificationToken', token.value);
        var cust = form.querySelector('input[name="CustomerId"]');
        if (cust && cust.value.trim()) body.append('customerId', cust.value.trim());
        cart.forEach(function (l) { body.append('variantIds', l.variantId); body.append('quantities', l.quantity); });
        var requestedSig = autoSig;

        fetch('/Pos/AutoDiscount', { method: 'POST', body: body, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error(); return r.json(); })
            .then(function (res) {
                if (appliedDiscount && !appliedDiscount.auto) return;   // a typed code took over meanwhile
                if (requestedSig !== autoSig) return;                   // basket changed again - a newer check is running
                if (res.valid) {
                    appliedDiscount = { code: res.code, amount: res.amount, label: res.label, auto: true };
                    setDiscountMessage(res.name + ' - ' + res.label + ' applied automatically.', true);
                    render();
                } else if (appliedDiscount && appliedDiscount.auto) {
                    appliedDiscount = null;
                    setDiscountMessage('', false);
                    render();
                }
            })
            .catch(function () { /* best effort - the server applies any automatic discount at checkout regardless */ });
    }

    function clearDiscount(message) {
        appliedDiscount = null;
        appliedSig = '';
        autoSig = '';
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
            if (appliedDiscount && !appliedDiscount.auto && discountCodeInput.value.trim().toUpperCase() !== appliedDiscount.code) clearDiscount('');
        });
        document.querySelectorAll('.discount-pick').forEach(function (btn) {
            btn.addEventListener('click', function () {
                discountCodeInput.value = btn.getAttribute('data-code');
                applyDiscount(false);
            });
        });
        var custField = document.querySelector('#checkoutForm input[name="CustomerId"]');
        if (custField) custField.addEventListener('change', function () { if (appliedDiscount && !appliedDiscount.auto) applyDiscount(true); else { autoSig = ''; render(); } });
    }

    document.getElementById('checkoutForm').addEventListener('submit', function (e) {
        if (cart.length === 0) {
            e.preventDefault();
            scanError.textContent = 'Add at least one item before checking out.';
            return;
        }
        // Mandatory: a customer ID or an email address. (The server enforces this too - this just saves the round trip.)
        if (!tillField('CustomerId').trim() && !tillField('ReceiptEmail').trim()) {
            e.preventDefault();
            scanError.textContent = 'Enter the customer ID or an email address before completing the sale.';
            var custInput = document.querySelector('#checkoutForm [name="CustomerId"]');
            if (custInput) custInput.focus();
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

    // Nothing posted back from the server -> come back to the till exactly as it was left (cart, customer, payment, code).
    if (cart.length === 0) {
        var saved = loadTill();
        if (saved) {
            cart = saved.cart;
            var f = function (n, v) { var el = document.querySelector('#checkoutForm [name="' + n + '"]'); if (el && v) el.value = v; };
            f('CustomerId', saved.customerId);
            f('ReceiptEmail', saved.receiptEmail);
            f('PaymentMethod', saved.paymentMethod);
            if (discountCodeInput && saved.discountCode && !discountCodeInput.value) discountCodeInput.value = saved.discountCode;
        }
    }

    render();

    // A rejected checkout posts the typed code back - re-check it against the restored basket.
    if (discountCodeInput && discountCodeInput.value.trim() && cart.length > 0) applyDiscount(false);
})();
