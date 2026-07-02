// Rules shared by the server (string) and client (DOM) renderers, so that markup
// produced on either side agrees about names, casing and which props are not attributes.

export const VOID_ELEMENTS = new Set([
    "area", "base", "br", "col", "embed", "hr", "img", "input",
    "link", "meta", "param", "source", "track", "wbr"
]);

export const RAW_TEXT_ELEMENTS = new Set(["script", "style"]);

// Props that never become attributes.
const RESERVED_PROPS = new Set(["children", "key", "ref", "dangerouslySetInnerHTML"]);

const PROP_ALIASES = {
    className: "class",
    htmlFor: "for",
    acceptCharset: "accept-charset",
    httpEquiv: "http-equiv",
    crossOrigin: "crossorigin",
    autoComplete: "autocomplete",
    autoFocus: "autofocus",
    autoPlay: "autoplay",
    noValidate: "novalidate",
    readOnly: "readonly",
    maxLength: "maxlength",
    minLength: "minlength",
    tabIndex: "tabindex",
    srcSet: "srcset",
    colSpan: "colspan",
    rowSpan: "rowspan",
    contentEditable: "contenteditable",
    spellCheck: "spellcheck",
    referrerPolicy: "referrerpolicy"
};

// CSS properties that take a bare number rather than a px length.
const UNITLESS_CSS = new Set([
    "animationIterationCount", "aspectRatio", "borderImageOutset", "borderImageSlice",
    "borderImageWidth", "boxFlex", "boxOrdinalGroup", "columnCount", "flex", "flexGrow",
    "flexPositive", "flexShrink", "flexNegative", "flexOrder", "gridArea", "gridRow",
    "gridColumn", "fontWeight", "lineClamp", "lineHeight", "opacity", "order", "orphans",
    "scale", "tabSize", "widows", "zIndex", "zoom", "fillOpacity", "strokeOpacity",
    "strokeWidth", "strokeMiterlimit", "strokeDasharray", "strokeDashoffset"
]);

export function isReservedProp(name) {
    return RESERVED_PROPS.has(name);
}

export function isEventProp(name) {
    return name.length > 2 && name.charCodeAt(0) === 111 /* o */ && name.startsWith("on")
        && name[2] === name[2].toUpperCase();
}

export function eventNameFor(propName) {
    return propName.slice(2).toLowerCase();
}

export function attributeNameFor(propName) {
    if (Object.prototype.hasOwnProperty.call(PROP_ALIASES, propName)) {
        return PROP_ALIASES[propName];
    }
    if (propName.startsWith("data-") || propName.startsWith("aria-")) {
        return propName;
    }
    // camelCase -> kebab-case for anything else that looks like a JSX-ism, but leave
    // already-lowercase names (href, src, id, ...) untouched.
    if (/[A-Z]/.test(propName)) {
        return propName.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
    }
    return propName;
}

export function hyphenateStyleName(name) {
    if (name.startsWith("--")) {
        return name;
    }
    return name.replace(/([a-z0-9])([A-Z])/g, "$1-$2").toLowerCase();
}

export function styleValueFor(name, value) {
    if (typeof value === "number" && value !== 0 && !UNITLESS_CSS.has(name)) {
        return value + "px";
    }
    return String(value);
}

export function styleToString(style) {
    if (typeof style === "string") {
        return style;
    }
    if (!style || typeof style !== "object") {
        return "";
    }
    const parts = [];
    for (const key of Object.keys(style)) {
        const value = style[key];
        if (value === null || value === undefined || value === false || value === "") {
            continue;
        }
        parts.push(hyphenateStyleName(key) + ":" + styleValueFor(key, value));
    }
    return parts.join(";");
}

export function classNameToString(value) {
    if (typeof value === "string") {
        return value;
    }
    if (Array.isArray(value)) {
        return value.filter(Boolean).map(classNameToString).join(" ");
    }
    if (value && typeof value === "object") {
        return Object.keys(value).filter((k) => value[k]).join(" ");
    }
    return value === null || value === undefined || value === false ? "" : String(value);
}

// Values that mean "do not emit this attribute at all".
export function isOmittedAttributeValue(value) {
    return value === null || value === undefined || value === false;
}
