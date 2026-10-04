/*
 * Footwear size picker for the Add / Edit Product forms.
 *
 * When the Category is "Shoes", every variant row's free-text Size box is replaced by a fixed UK size dropdown
 * (UK 1 ... UK 15) with an "Add custom size..." option at the bottom for anything outside the list (e.g. UK 7.5).
 * For every other category the original free-text box (with its S/M/L suggestions) is left exactly as it was.
 *
 * The original <input name="Variants[i].Size"> stays in the DOM and is still the field that is posted, so the
 * server-side model binding does not change. Needs window.FFFootwear = { category: 'Shoes', sizes: ['UK 1', ...] }.
 */
(function () {
    var cfg = window.FFFootwear || {};
    var sizes = cfg.sizes || [];
    var CUSTOM = '__custom';
    var category = document.getElementById('Category');
    var tbody = document.getElementById('variantRows');
    if (!category || !tbody || sizes.length === 0) return;

    function isFootwear() { return category.value === cfg.category; }

    // "7", "uk7", "UK-7", "uk 7.5" -> "UK 7" / "UK 7.5". Anything else is left alone for the server to reject.
    function normalise(v) {
        v = (v || '').trim();
        var m = /^(?:uk[\s\-]*)?(\d{1,2}(?:\.5)?)$/i.exec(v);
        return m ? 'UK ' + m[1] : v;
    }

    function sync(input, select) {
        if (!isFootwear()) {
            select.style.display = 'none';
            input.style.display = '';
            input.placeholder = 'e.g. M';
            return;
        }
        input.value = normalise(input.value);
        select.style.display = '';
        if (input.value && sizes.indexOf(input.value) !== -1) {
            select.value = input.value;
            input.style.display = 'none';
        } else if (input.value) {
            select.value = CUSTOM;          // an existing non-standard size, e.g. UK 7.5
            input.style.display = '';
            input.placeholder = 'e.g. UK 7.5';
        } else {
            select.value = '';
            input.style.display = 'none';
        }
    }

    function enhance(input) {
        if (input.dataset.ffEnhanced) return;
        input.dataset.ffEnhanced = '1';

        var select = document.createElement('select');
        select.className = input.className;
        select.style.cssText = input.style.cssText;
        select.setAttribute('aria-label', 'UK size');
        select.innerHTML = '<option value="">-- UK size --</option>' +
            sizes.map(function (s) { return '<option value="' + s + '">' + s + '</option>'; }).join('') +
            '<option value="' + CUSTOM + '">+ Add custom size...</option>';
        input.parentNode.insertBefore(select, input);

        select.addEventListener('change', function () {
            if (select.value === CUSTOM) {
                input.value = '';
                input.style.display = '';
                input.placeholder = 'e.g. UK 7.5';
                input.focus();
            } else {
                input.value = select.value;
                input.style.display = 'none';
            }
        });
        input.addEventListener('blur', function () {
            if (isFootwear()) sync(input, select);
        });

        input._ffSelect = select;
        sync(input, select);
    }

    function enhanceAll() {
        tbody.querySelectorAll('input.variant-size-input').forEach(enhance);
    }

    category.addEventListener('change', function () {
        tbody.querySelectorAll('input.variant-size-input').forEach(function (input) {
            if (input._ffSelect) sync(input, input._ffSelect);
        });
    });

    // Rows added with "+ Add Size/Colour" are enhanced as soon as they appear.
    new MutationObserver(enhanceAll).observe(tbody, { childList: true });
    enhanceAll();
})();
