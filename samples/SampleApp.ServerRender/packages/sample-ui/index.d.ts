import type { ButtonHTMLAttributes, ReactNode } from "react";

export function Button(props: ButtonHTMLAttributes<HTMLButtonElement> & { variant?: "primary" | "secondary" }): ReactNode;
export function Card(props: { title: string; children?: ReactNode }): ReactNode;
