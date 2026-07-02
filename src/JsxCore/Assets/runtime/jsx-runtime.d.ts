// Type surface for the built-in JsxCore JSX runtime.
//
// TypeScript resolves the `JSX` namespace from this module because views are compiled with
// `"jsx": "react-jsx"` and `"jsxImportSource": "@jsxcore/runtime"`.

export type JsxNode =
    | JsxElement
    | string
    | number
    | bigint
    | boolean
    | null
    | undefined
    | JsxNode[];

export interface JsxElement {
    $$typeof: symbol;
    type: unknown;
    props: Record<string, any>;
    key: string | number | null;
}

export type Component<P = {}> = (props: P) => JsxNode;

export declare const Fragment: unique symbol;
export declare const ELEMENT: unique symbol;

export declare function jsx(type: unknown, props: Record<string, any>, key?: string | number | null): JsxElement;
export declare function jsxs(type: unknown, props: Record<string, any>, key?: string | number | null): JsxElement;
export declare function createElement(type: unknown, props?: Record<string, any> | null, ...children: JsxNode[]): JsxElement;
export declare function isElement(value: unknown): value is JsxElement;

export interface CSSProperties {
    [property: string]: string | number | null | undefined;
}

type ClassName = string | false | null | undefined | ClassName[] | Record<string, unknown>;

export interface DOMAttributes {
    children?: JsxNode;
    key?: string | number | null;
    dangerouslySetInnerHTML?: { __html: string };

    onClick?: (event: any) => void;
    onDoubleClick?: (event: any) => void;
    onInput?: (event: any) => void;
    onChange?: (event: any) => void;
    onSubmit?: (event: any) => void;
    onFocus?: (event: any) => void;
    onBlur?: (event: any) => void;
    onKeyDown?: (event: any) => void;
    onKeyUp?: (event: any) => void;
    onMouseEnter?: (event: any) => void;
    onMouseLeave?: (event: any) => void;
    onMouseDown?: (event: any) => void;
    onMouseUp?: (event: any) => void;
    onPointerDown?: (event: any) => void;
    onPointerUp?: (event: any) => void;
}

export interface HTMLAttributes extends DOMAttributes {
    id?: string;
    /** Both spellings are accepted; plain `class` is usually the more natural one here. */
    class?: ClassName;
    className?: ClassName;
    style?: CSSProperties | string;
    title?: string;
    lang?: string;
    dir?: string;
    hidden?: boolean;
    tabIndex?: number;
    role?: string;
    slot?: string;
    contentEditable?: boolean | "true" | "false";
    draggable?: boolean;
    spellCheck?: boolean;
    [attribute: `data-${string}`]: any;
    [attribute: `aria-${string}`]: any;
}

interface AnchorAttributes extends HTMLAttributes {
    href?: string;
    target?: string;
    rel?: string;
    download?: string | boolean;
    referrerPolicy?: string;
}

interface ImgAttributes extends HTMLAttributes {
    src?: string;
    alt?: string;
    width?: number | string;
    height?: number | string;
    loading?: "eager" | "lazy";
    decoding?: "sync" | "async" | "auto";
    srcSet?: string;
    sizes?: string;
}

interface InputAttributes extends HTMLAttributes {
    type?: string;
    name?: string;
    value?: string | number;
    defaultValue?: string | number;
    placeholder?: string;
    checked?: boolean;
    disabled?: boolean;
    readOnly?: boolean;
    required?: boolean;
    autoComplete?: string;
    autoFocus?: boolean;
    min?: number | string;
    max?: number | string;
    step?: number | string;
    minLength?: number;
    maxLength?: number;
    pattern?: string;
    accept?: string;
    multiple?: boolean;
}

interface FormAttributes extends HTMLAttributes {
    action?: string;
    method?: string;
    encType?: string;
    noValidate?: boolean;
    target?: string;
}

interface ButtonAttributes extends HTMLAttributes {
    type?: "button" | "submit" | "reset";
    name?: string;
    value?: string | number;
    disabled?: boolean;
    form?: string;
}

interface SelectAttributes extends HTMLAttributes {
    name?: string;
    value?: string | number;
    disabled?: boolean;
    multiple?: boolean;
    required?: boolean;
    size?: number;
}

interface OptionAttributes extends HTMLAttributes {
    value?: string | number;
    selected?: boolean;
    disabled?: boolean;
    label?: string;
}

interface TextareaAttributes extends HTMLAttributes {
    name?: string;
    value?: string | number;
    placeholder?: string;
    rows?: number;
    cols?: number;
    disabled?: boolean;
    readOnly?: boolean;
    required?: boolean;
    maxLength?: number;
}

interface LabelAttributes extends HTMLAttributes {
    /** Both spellings are accepted, as with class and className. */
    for?: string;
    htmlFor?: string;
}

interface TableCellAttributes extends HTMLAttributes {
    colSpan?: number;
    rowSpan?: number;
    scope?: string;
    headers?: string;
}

interface LinkAttributes extends HTMLAttributes {
    rel?: string;
    href?: string;
    type?: string;
    as?: string;
    crossOrigin?: string;
    media?: string;
}

interface MetaAttributes extends HTMLAttributes {
    name?: string;
    content?: string;
    charSet?: string;
    httpEquiv?: string;
    property?: string;
}

interface ScriptAttributes extends HTMLAttributes {
    src?: string;
    type?: string;
    async?: boolean;
    defer?: boolean;
    noModule?: boolean;
    crossOrigin?: string;
    integrity?: string;
}

interface MediaAttributes extends HTMLAttributes {
    src?: string;
    controls?: boolean;
    autoPlay?: boolean;
    loop?: boolean;
    muted?: boolean;
    preload?: string;
    poster?: string;
}

interface TimeAttributes extends HTMLAttributes {
    dateTime?: string;
}

interface ProgressAttributes extends HTMLAttributes {
    value?: number | string;
    max?: number | string;
}

interface DetailsAttributes extends HTMLAttributes {
    open?: boolean;
}

interface OlAttributes extends HTMLAttributes {
    start?: number;
    reversed?: boolean;
    type?: string;
}

interface IframeAttributes extends HTMLAttributes {
    src?: string;
    srcDoc?: string;
    width?: number | string;
    height?: number | string;
    allow?: string;
    loading?: "eager" | "lazy";
    sandbox?: string;
    referrerPolicy?: string;
}

export namespace JSX {
    type Element = JsxElement;

    interface ElementChildrenAttribute {
        children: {};
    }

    interface IntrinsicAttributes {
        key?: string | number | null;
    }

    interface IntrinsicElements {
        a: AnchorAttributes;
        abbr: HTMLAttributes;
        address: HTMLAttributes;
        article: HTMLAttributes;
        aside: HTMLAttributes;
        audio: MediaAttributes;
        b: HTMLAttributes;
        base: LinkAttributes;
        bdi: HTMLAttributes;
        bdo: HTMLAttributes;
        blockquote: HTMLAttributes;
        body: HTMLAttributes;
        br: HTMLAttributes;
        button: ButtonAttributes;
        canvas: HTMLAttributes;
        caption: HTMLAttributes;
        cite: HTMLAttributes;
        code: HTMLAttributes;
        col: TableCellAttributes;
        colgroup: TableCellAttributes;
        data: HTMLAttributes;
        datalist: HTMLAttributes;
        dd: HTMLAttributes;
        del: HTMLAttributes;
        details: DetailsAttributes;
        dfn: HTMLAttributes;
        dialog: DetailsAttributes;
        div: HTMLAttributes;
        dl: HTMLAttributes;
        dt: HTMLAttributes;
        em: HTMLAttributes;
        embed: HTMLAttributes;
        fieldset: HTMLAttributes;
        figcaption: HTMLAttributes;
        figure: HTMLAttributes;
        footer: HTMLAttributes;
        form: FormAttributes;
        h1: HTMLAttributes;
        h2: HTMLAttributes;
        h3: HTMLAttributes;
        h4: HTMLAttributes;
        h5: HTMLAttributes;
        h6: HTMLAttributes;
        head: HTMLAttributes;
        header: HTMLAttributes;
        hgroup: HTMLAttributes;
        hr: HTMLAttributes;
        html: HTMLAttributes;
        i: HTMLAttributes;
        iframe: IframeAttributes;
        img: ImgAttributes;
        input: InputAttributes;
        ins: HTMLAttributes;
        kbd: HTMLAttributes;
        label: LabelAttributes;
        legend: HTMLAttributes;
        li: HTMLAttributes;
        link: LinkAttributes;
        main: HTMLAttributes;
        map: HTMLAttributes;
        mark: HTMLAttributes;
        menu: HTMLAttributes;
        meta: MetaAttributes;
        meter: ProgressAttributes;
        nav: HTMLAttributes;
        noscript: HTMLAttributes;
        object: HTMLAttributes;
        ol: OlAttributes;
        optgroup: OptionAttributes;
        option: OptionAttributes;
        output: LabelAttributes;
        p: HTMLAttributes;
        picture: HTMLAttributes;
        pre: HTMLAttributes;
        progress: ProgressAttributes;
        q: HTMLAttributes;
        rp: HTMLAttributes;
        rt: HTMLAttributes;
        ruby: HTMLAttributes;
        s: HTMLAttributes;
        samp: HTMLAttributes;
        script: ScriptAttributes;
        search: HTMLAttributes;
        section: HTMLAttributes;
        select: SelectAttributes;
        slot: HTMLAttributes;
        small: HTMLAttributes;
        source: MediaAttributes;
        span: HTMLAttributes;
        strong: HTMLAttributes;
        style: HTMLAttributes;
        sub: HTMLAttributes;
        summary: HTMLAttributes;
        sup: HTMLAttributes;
        table: HTMLAttributes;
        tbody: HTMLAttributes;
        td: TableCellAttributes;
        template: HTMLAttributes;
        textarea: TextareaAttributes;
        tfoot: HTMLAttributes;
        th: TableCellAttributes;
        thead: HTMLAttributes;
        time: TimeAttributes;
        title: HTMLAttributes;
        tr: HTMLAttributes;
        track: MediaAttributes;
        u: HTMLAttributes;
        ul: HTMLAttributes;
        var: HTMLAttributes;
        video: MediaAttributes;
        wbr: HTMLAttributes;

        // SVG is passed through untyped rather than omitted, so icons and charts work.
        svg: HTMLAttributes & Record<string, any>;
        path: HTMLAttributes & Record<string, any>;
        circle: HTMLAttributes & Record<string, any>;
        rect: HTMLAttributes & Record<string, any>;
        line: HTMLAttributes & Record<string, any>;
        polyline: HTMLAttributes & Record<string, any>;
        polygon: HTMLAttributes & Record<string, any>;
        ellipse: HTMLAttributes & Record<string, any>;
        g: HTMLAttributes & Record<string, any>;
        defs: HTMLAttributes & Record<string, any>;
        text: HTMLAttributes & Record<string, any>;
        tspan: HTMLAttributes & Record<string, any>;
        use: HTMLAttributes & Record<string, any>;
        symbol: HTMLAttributes & Record<string, any>;
        marker: HTMLAttributes & Record<string, any>;
        linearGradient: HTMLAttributes & Record<string, any>;
        radialGradient: HTMLAttributes & Record<string, any>;
        stop: HTMLAttributes & Record<string, any>;
        clipPath: HTMLAttributes & Record<string, any>;
        mask: HTMLAttributes & Record<string, any>;
    }
}
