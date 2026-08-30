import test from "node:test";
import assert from "node:assert/strict";
import { renderedRoots } from "../store.mjs";

test("nested active page keeps its navigation ancestors", () => {
  const bounds = (x, y, width, height) => ({ x, y, width, height });
  const roots = [
    {
      id: "shell",
      type: "MainShell",
      isVisible: true,
      windowBounds: bounds(0, 0, 1000, 600),
      children: [
        {
          id: "menu",
          type: "ContentPage",
          isVisible: true,
          windowBounds: bounds(0, 0, 200, 600),
          children: [
            {
              id: "menu-label",
              type: "Label",
              isVisible: true,
              windowBounds: bounds(10, 100, 180, 30),
            },
          ],
        },
        {
          id: "navigation",
          type: "NavigationPage",
          isVisible: true,
          windowBounds: bounds(200, 0, 800, 600),
          children: [
            {
              id: "controls",
              type: "ControlsPage",
              isVisible: true,
              windowBounds: bounds(200, 40, 800, 560),
              children: [
                {
                  id: "action",
                  type: "Button",
                  isVisible: true,
                  windowBounds: bounds(240, 120, 200, 40),
                },
                {
                  id: "status",
                  type: "Label",
                  isVisible: true,
                  windowBounds: bounds(240, 180, 200, 40),
                },
              ],
            },
          ],
        },
      ],
    },
  ];

  const rendered = renderedRoots(roots, bounds(0, 0, 1000, 600));
  const ids = [];
  const visit = (nodes) => {
    for (const node of nodes) {
      ids.push(node.id);
      visit(node.children || []);
    }
  };
  visit(rendered);

  assert.deepEqual(ids, ["shell", "navigation", "controls", "action", "status"]);
});

test("custom named current page drops collapsed page siblings", () => {
  const bounds = (x, y, width, height) => ({ x, y, width, height });
  const roots = [
    {
      id: "shell",
      type: "MainShell",
      isVisible: true,
      windowBounds: bounds(0, 0, 411, 914),
      children: [
        {
          id: "home",
          type: "MainPage",
          isVisible: true,
          windowBounds: bounds(0, 0, 411, 798),
          children: [{ id: "home-label", type: "Label", isVisible: true, windowBounds: bounds(0, 0, 200, 40) }],
        },
        {
          id: "goals",
          type: "GoalsPage",
          isVisible: true,
          windowBounds: bounds(0, 0, 411, 798),
          children: [
            {
              id: "stale-scroll",
              type: "ScrollView",
              isVisible: true,
              windowBounds: bounds(0, 0, 379, 698),
              children: [{ id: "stale-label", type: "Label", isVisible: true, windowBounds: bounds(20, 180, 200, 40) }],
            },
          ],
        },
        {
          id: "subtraction",
          type: "Subtraction",
          isVisible: true,
          windowBounds: bounds(0, 116, 411, 798),
          children: [
            { id: "operand", type: "Label", isVisible: true, windowBounds: bounds(20, 188, 164, 63) },
            { id: "option", type: "Button", isVisible: true, windowBounds: bounds(20, 674, 180, 86) },
            { id: "result", type: "Grid", isVisible: true, windowBounds: bounds(20, 480, 371, 108) },
          ],
        },
      ],
    },
  ];

  const rendered = renderedRoots(roots, bounds(0, 0, 411, 914));
  const ids = [];
  const visit = (nodes) => {
    for (const node of nodes) {
      ids.push(node.id);
      visit(node.children || []);
    }
  };
  visit(rendered);

  assert.deepEqual(ids, ["shell", "subtraction", "operand", "option", "result"]);
});
