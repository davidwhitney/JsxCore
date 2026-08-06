// Server entry for Preact. Loaded by the .NET host, which calls renderView/readHead and reads the
// markup and head they return; nothing above this layer knows how a view is rendered.

import { createElement } from "preact";
import { render } from "preact-render-to-string";
import { createServerEntry } from "./view-host.js";

const entry = createServerEntry(createElement, render);

export const renderView = entry.renderView;
export const readHead = entry.readHead;
