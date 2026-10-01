/*
 * Reusable address search box.
 *
 * Any element marked  <div data-address-autocomplete data-street="AddressLine1" data-city="City" ...>
 * becomes an address search box. When the user picks a suggestion, the inputs named in the data-*
 * attributes are filled in. Nothing else about the form changes, so if the search fails, the service is
 * unavailable or the user ignores the box, the form works exactly as plain typed fields.
 *
 * Two providers (chosen on the server, see AddressAutocompleteSettings; rendered by _AddressSearch.cshtml):
 *   "geoapify" - suggestions fetched from THIS site's /AddressSearch/Suggest (the key stays on the server)
 *   "google"   - Google Places autocomplete in the browser (needs window.FF_ADDRESS_SEARCH.key)
 */
(function () {
    'use strict';

    var boxes = document.querySelectorAll('[data-address-autocomplete]');
    var cfg = window.FF_ADDRESS_SEARCH;
    if (!boxes.length || !cfg || !cfg.provider) return;

    // ---------- shared helpers ----------

    var HINT_PROBLEM = '#b3261e';
    var HINT_NORMAL = '#6b6b6b';

    function hintEl(box) {
        return box.parentElement && box.parentElement.querySelector('[data-address-autocomplete-hint]');
    }

    function showHint(box, text, isProblem) {
        var hint = hintEl(box);
        if (!hint) return;
        if (!hint.dataset.original) hint.dataset.original = hint.textContent;
        hint.textContent = text || hint.dataset.original;
        hint.style.color = isProblem ? HINT_PROBLEM : HINT_NORMAL;
    }

    function setValue(id, value) {
        if (!id) return;
        var el = document.getElementById(id);
        if (!el) return;
        el.value = value;
        // Let any validation / other scripts on the page know the value changed.
        el.dispatchEvent(new Event('input', { bubbles: true }));
        el.dispatchEvent(new Event('change', { bubbles: true }));
    }

    function currentValue(id) {
        var el = id && document.getElementById(id);
        return el ? el.value : '';
    }

    // a = { street, suburb, city, province, postal } - any may be ''
    function fill(box, a) {
        var d = box.dataset;

        if (!a.street) {
            showHint(box, 'Please pick a full street address (with a street name), or type it in below.', true);
        } else {
            showHint(box, null, false);
        }

        setValue(d.street, a.street);

        // The suburb box may hold something the person typed (e.g. "Flat 4"), so only replace it when we have
        // a suburb, or when it still holds the suburb we filled in last time.
        if (a.suburb) {
            setValue(d.suburb, a.suburb);
            box.dataset.lastSuburb = a.suburb;
        } else if (d.suburb && box.dataset.lastSuburb && currentValue(d.suburb) === box.dataset.lastSuburb) {
            setValue(d.suburb, '');
            box.dataset.lastSuburb = '';
        }

        setValue(d.city, a.city);
        setValue(d.province, a.province);
        if (a.postal) setValue(d.postal, a.postal); // keep what's typed if the service has no code for this address
    }

    function unavailable(box) {
        showHint(box, 'Address search is unavailable right now - please type your address in below.', true);
    }

    // ---------- Geoapify (via our own server) ----------

    function initGeoapify(box) {
        var suggestUrl = box.dataset.suggestUrl;
        if (!suggestUrl) { unavailable(box); return; }

        var MIN_CHARS = 3;
        var DEBOUNCE_MS = 300;
        var uid = 'ffaddr-' + Math.random().toString(36).slice(2, 8);

        box.style.position = 'relative';

        var input = document.createElement('input');
        input.type = 'text'; // deliberately has no name, so it is never posted with the form
        input.id = uid;
        input.placeholder = 'Start typing your address...';
        input.autocomplete = 'off';
        input.setAttribute('role', 'combobox');
        input.setAttribute('aria-autocomplete', 'list');
        input.setAttribute('aria-expanded', 'false');
        input.setAttribute('aria-controls', uid + '-list');
        input.setAttribute('aria-label', 'Search for your address');
        input.style.cssText = 'width:100%;box-sizing:border-box;padding:10px 14px;border:1px solid #c9c4b8;border-radius:8px;font-size:0.9rem;';

        var list = document.createElement('ul');
        list.id = uid + '-list';
        list.setAttribute('role', 'listbox');
        list.style.cssText = 'display:none;position:absolute;left:0;right:0;top:100%;z-index:1000;margin:4px 0 0;padding:4px 0;' +
            'list-style:none;background:#fff;border:1px solid #c9c4b8;border-radius:8px;box-shadow:0 6px 18px rgba(0,0,0,.12);' +
            'max-height:280px;overflow:auto;';

        box.appendChild(input);
        box.appendChild(list);

        // Required by Geoapify's terms and by OpenStreetMap's licence.
        var hint = hintEl(box);
        if (hint && hint.parentElement) {
            var credit = document.createElement('div');
            credit.style.cssText = 'font-size:0.7rem;color:#8a8a8a;margin-top:2px;';
            credit.innerHTML = 'Address search by <a href="https://www.geoapify.com/" target="_blank" rel="noopener">Geoapify</a> &middot; ' +
                '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors';
            hint.parentElement.appendChild(credit);
        }

        var items = [];
        var active = -1;
        var timer = null;
        var controller = null;
        var cache = {};

        function close() {
            list.style.display = 'none';
            input.setAttribute('aria-expanded', 'false');
            input.removeAttribute('aria-activedescendant');
            active = -1;
        }

        function setActive(i) {
            active = i;
            Array.prototype.forEach.call(list.children, function (li, idx) {
                var on = idx === i;
                li.style.background = on ? '#f3eee3' : '';
                li.setAttribute('aria-selected', on ? 'true' : 'false');
                if (on) {
                    input.setAttribute('aria-activedescendant', li.id);
                    li.scrollIntoView({ block: 'nearest' });
                }
            });
        }

        function render() {
            list.innerHTML = '';
            if (!items.length) { close(); return; }

            items.forEach(function (it, idx) {
                var li = document.createElement('li');
                li.id = uid + '-opt-' + idx;
                li.setAttribute('role', 'option');
                li.setAttribute('aria-selected', 'false');
                li.style.cssText = 'padding:8px 14px;cursor:pointer;';

                var main = document.createElement('div');
                main.style.cssText = 'font-size:0.88rem;font-weight:600;';
                main.textContent = it.line1 || it.label; // textContent: never trust text from a web service as HTML

                var sub = document.createElement('div');
                sub.style.cssText = 'font-size:0.76rem;color:#6b6b6b;';
                sub.textContent = it.line2 || '';

                li.appendChild(main);
                if (it.line2) li.appendChild(sub);

                // mousedown (not click) so it fires before the input's blur closes the list
                li.addEventListener('mousedown', function (e) { e.preventDefault(); choose(idx); });
                li.addEventListener('mousemove', function () { if (active !== idx) setActive(idx); });
                list.appendChild(li);
            });

            list.style.display = 'block';
            input.setAttribute('aria-expanded', 'true');
            active = -1;
        }

        function choose(idx) {
            var it = items[idx];
            if (!it) return;
            input.value = it.label;
            close();
            fill(box, { street: it.street, suburb: it.suburb, city: it.city, province: it.province, postal: it.postal });
        }

        function search(q) {
            if (cache[q]) { items = cache[q]; render(); return; }

            if (controller) controller.abort();
            controller = typeof AbortController !== 'undefined' ? new AbortController() : null;

            fetch(suggestUrl + (suggestUrl.indexOf('?') === -1 ? '?' : '&') + 'q=' + encodeURIComponent(q), {
                credentials: 'same-origin',
                headers: { 'Accept': 'application/json' },
                signal: controller ? controller.signal : undefined
            }).then(function (res) {
                var type = res.headers.get('content-type') || '';
                if (res.redirected || type.indexOf('json') === -1) {
                    // Redirected to the sign-in page, or an error page came back instead of data.
                    throw { signedOut: true };
                }
                return res.json().then(function (data) { return { ok: res.ok, status: res.status, data: data }; });
            }).then(function (r) {
                if (!r.ok) throw { status: r.status, error: r.data && r.data.error };
                items = (r.data && r.data.items) || [];
                cache[q] = items;
                showHint(box, null, false);
                render();
            }).catch(function (err) {
                if (err && err.name === 'AbortError') return; // superseded by a newer search - not an error
                items = [];
                close();
                if (err && err.signedOut) {
                    showHint(box, 'Please sign in again to use address search, or type your address in below.', true);
                } else if (err && err.error === 'rate') {
                    showHint(box, 'You are searching very quickly - please slow down, or type your address in below.', true);
                } else {
                    unavailable(box);
                }
            });
        }

        input.addEventListener('input', function () {
            var q = input.value.replace(/\s+/g, ' ').trim();
            clearTimeout(timer);
            if (q.length < MIN_CHARS) {
                if (controller) controller.abort();
                items = [];
                close();
                return;
            }
            timer = setTimeout(function () { search(q); }, DEBOUNCE_MS);
        });

        input.addEventListener('keydown', function (e) {
            var open = list.style.display !== 'none' && items.length > 0;
            if (e.key === 'ArrowDown' && open) {
                e.preventDefault();
                setActive(active + 1 >= items.length ? 0 : active + 1);
            } else if (e.key === 'ArrowUp' && open) {
                e.preventDefault();
                setActive(active - 1 < 0 ? items.length - 1 : active - 1);
            } else if (e.key === 'Enter') {
                e.preventDefault(); // Enter must never submit the surrounding form from this box
                if (open && active >= 0) choose(active);
            } else if (e.key === 'Escape') {
                close();
            }
        });

        input.addEventListener('blur', function () { setTimeout(close, 150); });
    }

    // ---------- Google Places (browser) ----------

    function loadGoogle() {
        if (window.google && window.google.maps && window.google.maps.importLibrary) return Promise.resolve();
        if (window.__ffMapsPromise) return window.__ffMapsPromise;

        window.__ffMapsPromise = new Promise(function (resolve, reject) {
            window.__ffMapsReady = resolve;
            var s = document.createElement('script');
            s.src = 'https://maps.googleapis.com/maps/api/js?key=' + encodeURIComponent(cfg.key) +
                '&v=weekly&loading=async&libraries=places&callback=__ffMapsReady';
            s.async = true;
            s.onerror = function () { reject(new Error('Google Maps script failed to load')); };
            document.head.appendChild(s);
        });
        return window.__ffMapsPromise;
    }

    function pick(components, types) {
        for (var t = 0; t < types.length; t++) {
            for (var i = 0; i < components.length; i++) {
                if (components[i].types && components[i].types.indexOf(types[t]) !== -1) {
                    return components[i].longText || '';
                }
            }
        }
        return '';
    }

    function initGoogle(box) {
        return google.maps.importLibrary('places').then(function (lib) {
            var element = new lib.PlaceAutocompleteElement({
                includedRegionCodes: [(box.dataset.country || 'za').toLowerCase()]
            });
            element.style.width = '100%';
            element.style.colorScheme = 'light';

            element.addEventListener('gmp-select', function (event) {
                var place = event.placePrediction.toPlace();
                place.fetchFields({ fields: ['addressComponents'] }).then(function () {
                    var c = place.addressComponents;
                    if (!c) { showHint(box, "We couldn't read that address - please type it in below.", true); return; }

                    var street = (pick(c, ['street_number']) + ' ' + pick(c, ['route'])).trim();
                    fill(box, {
                        street: street,
                        suburb: pick(c, ['sublocality_level_1', 'sublocality', 'neighborhood']),
                        // South African addresses vary in which level Google calls the "city".
                        city: pick(c, ['locality', 'postal_town', 'sublocality_level_1', 'administrative_area_level_2']),
                        province: pick(c, ['administrative_area_level_1']),
                        postal: pick(c, ['postal_code'])
                    });
                }).catch(function () {
                    showHint(box, "We couldn't read that address - please type it in below.", true);
                });
            });

            element.addEventListener('gmp-error', function () { unavailable(box); });
            box.appendChild(element);
        });
    }

    // ---------- start ----------

    if (cfg.provider === 'geoapify') {
        Array.prototype.forEach.call(boxes, initGeoapify);
    } else if (cfg.provider === 'google' && cfg.key) {
        loadGoogle().then(function () {
            return Promise.all(Array.prototype.map.call(boxes, initGoogle));
        }).catch(function () {
            Array.prototype.forEach.call(boxes, unavailable);
        });
    }
})();
