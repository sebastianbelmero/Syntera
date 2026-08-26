import type { WeatherForecast } from "../types";

export const fetchWeatherForecast = async (): Promise<WeatherForecast[]> => {
  const res = await fetch('/api/weatherforecast');
  
  if (!res.ok) {
    throw new Error(`Request failed: ${res.status}`);
  }
  
  return res.json();
};