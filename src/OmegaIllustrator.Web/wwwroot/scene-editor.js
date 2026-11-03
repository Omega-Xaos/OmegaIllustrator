const activeScenes = new WeakMap();

function toScenePoint(svg, event) {
    const point = svg.createSVGPoint();
    point.x = event.clientX;
    point.y = event.clientY;
    return point.matrixTransform(svg.getScreenCTM().inverse());
}

export function enableSceneDragging(sceneId, dotnet) {
    const svg = document.getElementById(sceneId);
    if (!svg || activeScenes.has(svg)) return;

    let drag = null;
    const onPointerDown = event => {
        if (event.button !== 0) return;
        const group = event.target.closest("[data-scene-item]");
        if (!group || !svg.contains(group)) return;

        event.preventDefault();
        const point = toScenePoint(svg, event);
        drag = {
            group,
            itemId: group.dataset.sceneItem,
            pointerId: event.pointerId,
            startX: Number(group.dataset.x),
            startY: Number(group.dataset.y),
            offsetX: Number(group.dataset.x) - point.x,
            offsetY: Number(group.dataset.y) - point.y,
            x: Number(group.dataset.x),
            y: Number(group.dataset.y),
            moved: false
        };
        group.setPointerCapture(event.pointerId);
    };

    const onPointerMove = event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        const point = toScenePoint(svg, event);
        drag.x = point.x + drag.offsetX;
        drag.y = point.y + drag.offsetY;
        drag.moved ||= Math.hypot(drag.x - drag.startX, drag.y - drag.startY) > 0.5;
        drag.group.setAttribute("transform", `translate(${drag.x} ${drag.y})`);
    };

    const onPointerUp = async event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        const completed = drag;
        drag = null;
        try {
            if (completed.group.hasPointerCapture(event.pointerId)) completed.group.releasePointerCapture(event.pointerId);
            await dotnet.invokeMethodAsync("SceneItemMoved", completed.itemId, completed.x, completed.y);
        } catch {
            completed.group.setAttribute("transform", `translate(${completed.startX} ${completed.startY})`);
        }
    };

    const onPointerCancel = event => {
        if (!drag || drag.pointerId !== event.pointerId) return;
        drag.group.setAttribute("transform", `translate(${drag.startX} ${drag.startY})`);
        drag = null;
    };

    svg.addEventListener("pointerdown", onPointerDown);
    svg.addEventListener("pointermove", onPointerMove);
    svg.addEventListener("pointerup", onPointerUp);
    svg.addEventListener("pointercancel", onPointerCancel);
    activeScenes.set(svg, () => {
        svg.removeEventListener("pointerdown", onPointerDown);
        svg.removeEventListener("pointermove", onPointerMove);
        svg.removeEventListener("pointerup", onPointerUp);
        svg.removeEventListener("pointercancel", onPointerCancel);
        activeScenes.delete(svg);
    });
}

export function disableSceneDragging(sceneId) {
    const svg = document.getElementById(sceneId);
    if (svg) activeScenes.get(svg)?.();
}
