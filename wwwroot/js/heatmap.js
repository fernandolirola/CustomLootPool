const views = new WeakMap();
const dialogs = new WeakMap();

export function detach(svg) {
    const state = views.get(svg);
    if (!state) return;
    state.cleanup();
    if (state.dialog) dialogs.delete(state.dialog);
    views.delete(svg);
}

export function attach(svg, height) {
    const dialog = svg.closest('[role=dialog]');
    if (dialog) detach(dialogs.get(dialog));
    detach(svg);
    const original = [0, 0, 1000, height];
    let box = [...original], dragging = null;
    // Markers keep a readable size on screen: they shrink a little as you zoom out and grow a little as you zoom in
    const draw = () => {
        svg.setAttribute('viewBox', box.join(' '));
        svg.style.setProperty('--clp-mark', Math.pow(box[2] / original[2], 0.7).toFixed(4));
    };
    const host = svg.parentElement;
    const tip = document.createElement('div');
    tip.className = 'clp-atlas-tip';
    tip.hidden = true;
    host.appendChild(tip);
    const line = (cls, text) => { const el = document.createElement('div'); el.className = cls; el.textContent = text; return el; };
    const fill = target => {
        tip.replaceChildren();
        if (target.classList.contains('clp-atlas-box')) {
            for (const kind of (target.dataset.tipItems || '').split('|').filter(Boolean)) {
                const [name, tpl, count] = kind.split(';');
                const row = document.createElement('div');
                row.className = 'clp-atlas-tip-row';
                if (tpl) {
                    const img = document.createElement('img');
                    // Container pictures ship with the mod (wwwroot/containers); unknown containers simply show no picture
                    img.src = `/CustomLootPool/containers/${tpl}.webp`;
                    img.alt = '';
                    img.onerror = () => img.remove();
                    row.appendChild(img);
                }
                const copy = document.createElement('span');
                copy.textContent = Number(count) > 1 ? `${name} ×${count}` : name;
                row.appendChild(copy);
                tip.appendChild(row);
            }
            if (target.dataset.tipExtra) tip.appendChild(line('clp-atlas-tip-extra', target.dataset.tipExtra));
        } else {
            const [name, weight, where] = (target.dataset.tip || '').split('|');
            tip.appendChild(line('clp-atlas-tip-title', name));
            if (weight) tip.appendChild(line('clp-atlas-tip-extra', weight));
            if (where) tip.appendChild(line('clp-atlas-tip-extra', where));
        }
    };
    const place = event => {
        const area = host.getBoundingClientRect();
        let x = event.clientX - area.left + 14, y = event.clientY - area.top + 14;
        if (x + tip.offsetWidth > area.width - 8) x = event.clientX - area.left - tip.offsetWidth - 14;
        if (y + tip.offsetHeight > area.height - 8) y = event.clientY - area.top - tip.offsetHeight - 14;
        tip.style.transform = `translate(${Math.max(4, x)}px, ${Math.max(4, y)}px)`;
    };
    let hovered = null;
    const over = event => {
        if (dragging) return;
        const target = event.target.closest?.('.clp-atlas-box, .clp-atlas-spot');
        if (!target || !svg.contains(target)) return;
        if (target !== hovered) { hovered = target; fill(target); }
        tip.hidden = false;
        place(event);
    };
    const out = event => {
        const target = event.target.closest?.('.clp-atlas-box, .clp-atlas-spot');
        if (target && !target.contains(event.relatedTarget)) { hovered = null; tip.hidden = true; }
    };
    const hoverMove = event => { if (!tip.hidden) place(event); };
    const focusIn = event => {
        const target = event.target.closest?.('.clp-atlas-box');
        if (!target) return;
        hovered = target; fill(target); tip.hidden = false;
        const r = target.getBoundingClientRect();
        place({ clientX: r.right, clientY: r.bottom });
    };
    const focusOut = () => { hovered = null; tip.hidden = true; };
    const position = event => {
        const point = svg.createSVGPoint();
        point.x = event.clientX; point.y = event.clientY;
        return point.matrixTransform(svg.getScreenCTM().inverse());
    };
    const wheel = event => {
        event.preventDefault();
        const point = position(event);
        const ratio = Math.max(.08, Math.min(1.5, box[2] / original[2] * Math.exp(event.deltaY * .0015)));
        const width = original[2] * ratio, factor = width / box[2];
        box = [point.x - (point.x - box[0]) * factor, point.y - (point.y - box[1]) * factor, width, original[3] * ratio];
        draw();
    };
    const down = event => { if (event.button !== 0) return; dragging = position(event); tip.hidden = true; svg.setPointerCapture(event.pointerId); svg.classList.add('dragging'); };
    const move = event => {
        if (!dragging) return;
        const point = position(event);
        box[0] += dragging.x - point.x; box[1] += dragging.y - point.y;
        draw();
    };
    const up = () => { dragging = null; svg.classList.remove('dragging'); };
    svg.addEventListener('wheel', wheel, { passive: false });
    svg.addEventListener('pointerdown', down); svg.addEventListener('pointermove', move);
    svg.addEventListener('pointerup', up); svg.addEventListener('pointercancel', up);
    svg.addEventListener('pointerover', over); svg.addEventListener('pointerout', out); svg.addEventListener('pointermove', hoverMove);
    svg.addEventListener('focusin', focusIn); svg.addEventListener('focusout', focusOut);
    if (dialog && !dialog.contains(document.activeElement)) dialog.focus({ preventScroll: true });
    const trap = event => {
        if (event.key !== 'Tab') return;
        const controls = [...dialog.querySelectorAll('button,select,input,[tabindex="0"]')].filter(e => !e.disabled && e.offsetParent !== null);
        if (!controls.length) return;
        if (event.shiftKey && (document.activeElement === controls[0] || document.activeElement === dialog)) { controls.at(-1).focus(); event.preventDefault(); }
        else if (!event.shiftKey && document.activeElement === controls.at(-1)) { controls[0].focus(); event.preventDefault(); }
    };
    dialog?.addEventListener('keydown', trap);
    views.set(svg, {
        dialog,
        reset() { box = [...original]; draw(); },
        cleanup() {
            svg.removeEventListener('wheel', wheel); svg.removeEventListener('pointerdown', down);
            svg.removeEventListener('pointermove', move); svg.removeEventListener('pointerup', up);
            svg.removeEventListener('pointercancel', up); dialog?.removeEventListener('keydown', trap);
            svg.removeEventListener('pointerover', over); svg.removeEventListener('pointerout', out); svg.removeEventListener('pointermove', hoverMove);
            svg.removeEventListener('focusin', focusIn); svg.removeEventListener('focusout', focusOut);
            tip.remove();
        }
    });
    if (dialog) dialogs.set(dialog, svg);
    draw();
}

export function reset(svg) { views.get(svg)?.reset(); }
