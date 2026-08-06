// Server entry for React. Loaded by the .NET host, which calls renderView/readHead and reads the
// markup and head they return; the contract is identical to every other framework's entry.

import React from "react";
import ReactDomServer from "react-dom/server.browser";
import { createServerEntry } from "./view-host.js";

const entry = createServerEntry(React.createElement, ReactDomServer.renderToString);

export const renderView = entry.renderView;
export const readHead = entry.readHead;
