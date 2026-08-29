import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { App } from "./app/App";
import "./app/styles.css";
import { telegram } from "./telegram/webApp";
import { ToastViewport } from "./components/Ui";

telegram.initialize();
createRoot(document.getElementById("root")!).render(<StrictMode><App /><ToastViewport /></StrictMode>);
