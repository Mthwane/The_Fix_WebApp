/* Fashion Fix charts - thin wrapper over Chart.js (loaded from cdnjs before this file).
   Every chart has: a visible grid, hover tooltips with the exact rand value, and on pie/donut
   charts the percentage share as well. Usage: ffCharts.line('id', labels, values, 'Revenue') etc. */
(function () {
    var PALETTE = ['#2f5943', '#c9a227', '#7a9385', '#a0522d', '#5b7a68', '#d9c98a', '#3d6b8c', '#8c4f6b', '#6b8e23', '#b8703e'];
    var GRID = 'rgba(0,0,0,0.08)';
    var TEXT = '#4a4a4a';

    function money(v) {
        var n = Number(v) || 0;
        return 'R' + n.toLocaleString('en-ZA', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).replace(/ /g, ' ');
    }
    function shortMoney(v) {
        var n = Number(v) || 0;
        if (Math.abs(n) >= 1000000) return 'R' + (n / 1000000).toFixed(1) + 'm';
        if (Math.abs(n) >= 1000) return 'R' + (n / 1000).toFixed(n >= 10000 ? 0 : 1) + 'k';
        return 'R' + n.toFixed(0);
    }
    function ctx(id) {
        var el = document.getElementById(id);
        if (!el || typeof Chart === 'undefined') return null;
        return el.getContext('2d');
    }
    function empty(id, msg) {
        var el = document.getElementById(id);
        if (!el) return;
        var box = document.createElement('div');
        box.style.cssText = 'display:flex;align-items:center;justify-content:center;height:' + (el.getAttribute('height') || 180) + 'px;color:#777;font-size:.85rem;';
        box.textContent = msg || 'Not enough data in this range yet.';
        el.parentNode.replaceChild(box, el);
    }
    function hasData(values) {
        return values && values.length > 0 && values.some(function (v) { return Number(v) > 0; });
    }
    var base = {
        responsive: true,
        maintainAspectRatio: false,
        interaction: { mode: 'index', intersect: false },
        font: { family: 'inherit' }
    };

    window.ffCharts = {
        palette: PALETTE,
        money: money,

        line: function (id, labels, values, label) {
            if (!hasData(values) || labels.length < 2) return empty(id);
            var c = ctx(id); if (!c) return;
            new Chart(c, {
                type: 'line',
                data: {
                    labels: labels,
                    datasets: [{
                        label: label || 'Revenue', data: values, borderColor: PALETTE[0], backgroundColor: 'rgba(47,89,67,0.15)',
                        fill: true, tension: 0.3, pointRadius: 3, pointHoverRadius: 6, borderWidth: 2.5
                    }]
                },
                options: Object.assign({}, base, {
                    plugins: {
                        legend: { display: false },
                        tooltip: { callbacks: { label: function (t) { return ' ' + t.dataset.label + ': ' + money(t.parsed.y); } } }
                    },
                    scales: {
                        x: { grid: { color: GRID }, ticks: { color: TEXT, maxRotation: 0, autoSkip: true, maxTicksLimit: 10 } },
                        y: { beginAtZero: true, grid: { color: GRID }, ticks: { color: TEXT, callback: function (v) { return shortMoney(v); } } }
                    }
                })
            });
        },

        donut: function (id, labels, values) {
            if (!hasData(values)) return empty(id, 'No data yet.');
            var c = ctx(id); if (!c) return;
            var total = values.reduce(function (a, b) { return a + Number(b); }, 0);
            new Chart(c, {
                type: 'doughnut',
                data: { labels: labels, datasets: [{ data: values, backgroundColor: PALETTE, borderColor: '#fff', borderWidth: 2, hoverOffset: 8 }] },
                options: {
                    responsive: true, maintainAspectRatio: false, cutout: '62%',
                    plugins: {
                        legend: { display: false },
                        tooltip: {
                            callbacks: {
                                label: function (t) {
                                    var v = Number(t.parsed) || 0;
                                    var pct = total > 0 ? (v / total * 100).toFixed(1) : '0.0';
                                    return ' ' + t.label + ': ' + money(v) + '  (' + pct + '%)';
                                }
                            }
                        }
                    }
                }
            });
        },

        /* datasets: [{label, data}], options: {stacked:bool, horizontal:bool, money:bool} */
        bar: function (id, labels, datasets, opts) {
            opts = opts || {};
            var any = datasets.some(function (d) { return hasData(d.data); });
            if (!any) return empty(id, 'No data yet.');
            var c = ctx(id); if (!c) return;
            var isMoney = opts.money !== false;
            var horizontal = !!opts.horizontal;
            var valueAxis = horizontal ? 'x' : 'y';
            var scales = {};
            scales[horizontal ? 'y' : 'x'] = { stacked: !!opts.stacked, grid: { display: false }, ticks: { color: TEXT } };
            scales[valueAxis] = {
                stacked: !!opts.stacked, beginAtZero: true, grid: { color: GRID },
                ticks: { color: TEXT, callback: function (v) { return isMoney ? shortMoney(v) : v; } }
            };
            new Chart(c, {
                type: 'bar',
                data: {
                    labels: labels,
                    datasets: datasets.map(function (d, i) {
                        return { label: d.label, data: d.data, backgroundColor: d.color || PALETTE[i % PALETTE.length], borderRadius: 4, maxBarThickness: 46 };
                    })
                },
                options: Object.assign({}, base, {
                    indexAxis: horizontal ? 'y' : 'x',
                    plugins: {
                        legend: { display: datasets.length > 1, labels: { color: TEXT, boxWidth: 12 } },
                        tooltip: {
                            callbacks: {
                                label: function (t) {
                                    var v = horizontal ? t.parsed.x : t.parsed.y;
                                    return ' ' + t.dataset.label + ': ' + (isMoney ? money(v) : v);
                                }
                            }
                        }
                    },
                    scales: scales
                })
            });
        }
    };
})();
