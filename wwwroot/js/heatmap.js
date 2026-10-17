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
    const draw = () => svg.setAttribute('viewBox', box.join(' '));
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
    const down = event => { if (event.button !== 0) return; dragging = position(event); svg.setPointerCapture(event.pointerId); svg.classList.add('dragging'); };
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
        }
    });
    if (dialog) dialogs.set(dialog, svg);
    draw();
}

export function reset(svg) { views.get(svg)?.reset(); }
