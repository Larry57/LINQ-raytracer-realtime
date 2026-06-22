"use strict";
importScripts("engine.js");

let pixels = null, W = 0, H = 0;

self.onmessage = (e) => {
  const m = e.data;
  if (m.type === "init") {
    W = m.W; H = m.H;
    pixels = new Uint8ClampedArray(m.sab);   // vue sur la mémoire partagée
    return;
  }
  if (m.type === "render") {
    // m.cam et m.ball sont des objets simples {x,y,z} (clonés via postMessage) :
    // les helpers du moteur lisent .x/.y/.z, donc ça fonctionne tel quel.
    const scene = RT.createScene(m.cam, m.ball);
    RT.renderBand(scene, pixels, W, H, m.y0, m.y1);
    self.postMessage({ type: "done", id: m.id, band: m.band });
  }
};
